using System.Collections.Concurrent;
using Comet.Data.Characters;
using Comet.Tickets;
using Microsoft.Extensions.Logging;

namespace Comet.Server.Joining;

/// <summary>Why a join failed.</summary>
public enum JoinFailure
{
    /// <summary>Malformed, wrongly signed or for an unknown key.</summary>
    BadTicket,

    /// <summary>Expired, or issued before this server started: fetch a new one.</summary>
    ExpiredTicket,

    UsedTicket,
    WrongServer,

    /// <summary>The character is missing or deleted.</summary>
    NoCharacter,

    /// <summary>The database didn't hand over the character within the claim deadline.</summary>
    Busy,

    /// <summary>A newer join for the same character came in while this one was still claiming.</summary>
    Replaced,
}

/// <summary>The game's side of <see cref="JoinedCharacters{TConnection, TCharacter}"/>, called on the tick thread.</summary>
public interface IJoinHost<TConnection, TCharacter>
    where TCharacter : Character
{
    /// <summary>The join failed: tell the client why.</summary>
    void JoinFailed(TConnection connection, JoinFailure failure);

    /// <summary>
    /// The character is claimed for the connection: put it in the world, at <paramref name="claim"/>'s position or the
    /// game's fallback.
    /// </summary>
    void Joined(TConnection connection, CharacterClaim<TCharacter> claim);

    /// <summary>
    /// Takes the connection out of the world and drops it, because a newer join (<paramref name="replaced"/>) or the
    /// database (<paramref name="refused"/>: a ban, a deletion, or another server's claim) took its character.
    /// Returns where it was, for its final save (null when refused, since the save would be refused too).
    /// </summary>
    CharacterPosition? Dropped(TConnection connection, bool replaced);
}

/// <summary>
/// Which connection plays which character, for one game server. Joins check the ticket and claim the character off
/// the tick thread, then come back through <see cref="Update"/>; a newer join for a character drops the connection
/// that had it, whose final save goes before the new claim; and saves the database refuses drop the connection they
/// were for. Everything but the background work runs on the tick thread.
/// </summary>
/// <typeparam name="TConnection">The game's connection (a reference type, compared by reference).</typeparam>
public sealed class JoinedCharacters<TConnection, TCharacter>
    where TConnection : class
    where TCharacter : Character
{
    private readonly TicketChecker? _tickets; // none on a development server that joins without tickets
    private readonly ICharacterRepository<TCharacter> _repository;
    private readonly TimeSpan _claimDeadline; // for the database to hand over a character, or the join fails as Busy
    private readonly ILogger _logger;

    // Finished background work, for the tick thread.
    private readonly ConcurrentQueue<Action<IJoinHost<TConnection, TCharacter>>> _done = new();

    // Joins in progress (ticket being checked or character being claimed), and who plays what.
    private readonly Dictionary<TConnection, Pending> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TConnection, Binding> _joined = new(ReferenceEqualityComparer.Instance);

    // The connection that has (or is claiming) each character: the newest join.
    private readonly Dictionary<long, TConnection> _holders = [];

    public JoinedCharacters(
        TicketChecker? tickets, ICharacterRepository<TCharacter> repository, TimeSpan claimDeadline, ILogger logger)
    {
        _tickets = tickets;
        _repository = repository;
        _claimDeadline = claimDeadline;
        _logger = logger;
        repository.SaveRefused += refused => _done.Enqueue(host => Refuse(host, refused));
    }

    /// <summary>Connections joined to a character.</summary>
    public int Count => _joined.Count;

    /// <summary>Starts a join with a ticket. A connection already joined or joining is refused (send one join per connection).</summary>
    public bool Begin(TConnection connection, string ticket)
    {
        if (_pending.ContainsKey(connection) || _joined.ContainsKey(connection))
        {
            return false;
        }

        var pending = new Pending();
        _pending.Add(connection, pending);
        _ = Task.Run(async () =>
        {
            var (checkedTicket, rejection) = _tickets == null
                ? (null, TicketRejection.Invalid)
                : await _tickets.CheckAsync(ticket);
            _done.Enqueue(host => TicketChecked(host, connection, pending, checkedTicket, rejection));
        });
        return true;
    }

    /// <summary>
    /// Starts a join without a ticket, for a character the game has already picked (a development server without
    /// accounts). The character must not be playing or joining (<see cref="IsPlaying"/>): this never replaces anyone.
    /// </summary>
    public bool Begin(TConnection connection, long characterId, long accountId)
    {
        if (_pending.ContainsKey(connection) || _joined.ContainsKey(connection) || IsPlaying(characterId))
        {
            return false;
        }

        var pending = new Pending { CharacterId = characterId };
        _pending.Add(connection, pending);
        StartClaim(connection, pending, characterId, accountId);
        return true;
    }

    /// <summary>Whether a connection plays the character, or is joining as it.</summary>
    public bool IsPlaying(long characterId) => _holders.ContainsKey(characterId);

    /// <summary>Runs finished joins, refused saves and drops. Call once per tick.</summary>
    public void Update(IJoinHost<TConnection, TCharacter> host)
    {
        while (_done.TryDequeue(out var work))
        {
            work(host);
        }
    }

    /// <summary>What the connection plays as, if it has joined.</summary>
    public bool TryGet(TConnection connection, out long characterId, out long accountId)
    {
        var found = _joined.TryGetValue(connection, out var binding);
        characterId = found ? binding!.CharacterId : 0;
        accountId = found ? binding!.AccountId : 0;
        return found;
    }

    /// <summary>Queues a status save for the connection's character (nothing if it hasn't joined).</summary>
    public void SaveStatus(TConnection connection, CharacterPosition position)
    {
        if (_joined.TryGetValue(connection, out var binding))
        {
            _repository.SaveStatus(binding.CharacterId, binding.Version, position);
        }
    }

    /// <summary>Queues a save of the game's own columns for the connection's character.</summary>
    public void SaveCharacter(TConnection connection, TCharacter character)
    {
        if (_joined.TryGetValue(connection, out var binding) && binding.CharacterId == character.Id)
        {
            _repository.SaveCharacter(character, binding.Version);
        }
    }

    /// <summary>The connection left (or was dropped by the game): writes its final save and releases its character.</summary>
    public Task Leave(TConnection connection, CharacterPosition? position)
    {
        if (_pending.Remove(connection, out var pending))
        {
            pending.Abandoned = true;
            ForgetHolder(connection, pending.CharacterId);
        }

        if (!_joined.Remove(connection, out var binding))
        {
            return Task.CompletedTask;
        }

        ForgetHolder(connection, binding.CharacterId);
        return _repository.LeaveAsync(binding.CharacterId, binding.Version, position);
    }

    /// <summary>Every joined connection leaves, with their final positions (on shutdown).</summary>
    public Task LeaveAll(Func<TConnection, CharacterPosition?> positionOf) =>
        Task.WhenAll(_joined.Keys.ToList().Select(connection => Leave(connection, positionOf(connection))));

    private void TicketChecked(
        IJoinHost<TConnection, TCharacter> host, TConnection connection, Pending pending, JoinTicket? ticket, TicketRejection? rejection)
    {
        if (pending.Abandoned)
        {
            return; // the connection closed meanwhile
        }

        if (ticket == null)
        {
            _pending.Remove(connection);
            host.JoinFailed(connection, rejection switch
            {
                TicketRejection.Expired => JoinFailure.ExpiredTicket,
                TicketRejection.Used => JoinFailure.UsedTicket,
                TicketRejection.WrongServer => JoinFailure.WrongServer,
                _ => JoinFailure.BadTicket,
            });
            return;
        }

        pending.CharacterId = ticket.CharacterId;

        // The newer join wins: the older connection leaves first, so its final save is queued before this claim.
        if (_holders.TryGetValue(ticket.CharacterId, out var older))
        {
            if (_joined.ContainsKey(older))
            {
                var position = host.Dropped(older, replaced: true);
                _ = Leave(older, position);
            }
            else if (_pending.Remove(older, out var olderPending))
            {
                olderPending.Abandoned = true;
                host.JoinFailed(older, JoinFailure.Replaced);
            }
        }

        StartClaim(connection, pending, ticket.CharacterId, ticket.AccountId);
    }

    private void StartClaim(TConnection connection, Pending pending, long characterId, long accountId)
    {
        _holders[characterId] = connection;
        _ = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(_claimDeadline);
            var claim = await _repository.ClaimAsync(characterId, deadline.Token);
            _done.Enqueue(h => Claimed(h, connection, pending, characterId, accountId, claim));
        });
    }

    private void Claimed(IJoinHost<TConnection, TCharacter> host, TConnection connection, Pending pending,
        long characterId, long accountId, CharacterClaim<TCharacter> claim)
    {
        if (pending.Abandoned)
        {
            // Closed or replaced while claiming: let the character go (refused harmlessly if a newer claim took it).
            if (claim.Outcome == ClaimOutcome.Claimed)
            {
                _ = _repository.LeaveAsync(characterId, claim.Version, null);
            }

            return;
        }

        _pending.Remove(connection);
        if (claim.Outcome != ClaimOutcome.Claimed)
        {
            ForgetHolder(connection, characterId);
            host.JoinFailed(connection, claim.Outcome == ClaimOutcome.NotFound ? JoinFailure.NoCharacter : JoinFailure.Busy);
            return;
        }

        if (claim.Character!.AccountId != accountId)
        {
            // A ticket naming someone else's character: never issued by our Login server.
            _logger.LogWarning("A join for account {Account} named character {Character} of another account", accountId, characterId);
            _ = _repository.LeaveAsync(characterId, claim.Version, null);
            ForgetHolder(connection, characterId);
            host.JoinFailed(connection, JoinFailure.BadTicket);
            return;
        }

        _joined.Add(connection, new Binding(characterId, accountId, claim.Version));
        host.Joined(connection, claim);
    }

    private void Refuse(IJoinHost<TConnection, TCharacter> host, SaveRefused refused)
    {
        // Only the connection playing that character under that version: a newer one keeps playing.
        if (_holders.TryGetValue(refused.CharacterId, out var connection)
            && _joined.TryGetValue(connection, out var binding)
            && binding.Version == refused.Version)
        {
            host.Dropped(connection, replaced: false);
            _joined.Remove(connection);
            ForgetHolder(connection, refused.CharacterId);
        }
    }

    private void ForgetHolder(TConnection connection, long characterId)
    {
        if (_holders.TryGetValue(characterId, out var holder) && ReferenceEquals(holder, connection))
        {
            _holders.Remove(characterId);
        }
    }

    private sealed class Pending
    {
        public long CharacterId { get; set; }
        public bool Abandoned { get; set; }
    }

    private sealed record Binding(long CharacterId, long AccountId, long Version);
}
