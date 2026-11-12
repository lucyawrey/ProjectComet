using System.Collections.Concurrent;
using Comet.Data.Characters;
using Comet.Tickets;
using Microsoft.Extensions.Logging;

namespace Comet.Server.Joining;

public enum JoinFailure
{
    BadTicket,

    ExpiredTicket,

    UsedTicket,
    WrongServer,

    NoCharacter,

    Busy,

    Replaced,
}

public interface IJoinHost<TConnection, TCharacter>
    where TCharacter : Character
{
    void JoinFailed(TConnection connection, JoinFailure failure);

    void Joined(TConnection connection, CharacterClaim<TCharacter> claim);

    /// <summary>Drops a connection whose character was taken by a newer join or the database; returns its place for a final save, or null.</summary>
    CharacterPosition? Dropped(TConnection connection, bool replaced);
}

/// <summary>Ticket check and claim run off the tick thread and return through <see cref="Update"/>; all else runs on the tick thread.</summary>
/// <typeparam name="TConnection">The game's connection (a reference type, compared by reference).</typeparam>
public sealed class JoinedCharacters<TConnection, TCharacter>
    where TConnection : class
    where TCharacter : Character
{
    private readonly TicketChecker? _tickets;
    private readonly ICharacterRepository<TCharacter> _repository;
    private readonly TimeSpan _claimDeadline;
    private readonly ILogger _logger;

    private readonly ConcurrentQueue<Action<IJoinHost<TConnection, TCharacter>>> _done = new();

    private readonly Dictionary<TConnection, Pending> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TConnection, Binding> _joined = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<long, TConnection> _holders = [];

    public JoinedCharacters(TicketChecker? tickets, ICharacterRepository<TCharacter> repository, TimeSpan claimDeadline, ILogger logger)
    {
        _tickets = tickets;
        _repository = repository;
        _claimDeadline = claimDeadline;
        _logger = logger;
        repository.SaveRefused += refused => _done.Enqueue(host => Refuse(host, refused));
    }

    public int Count => _joined.Count;

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
            var (checkedTicket, rejection) = _tickets == null ? (null, TicketRejection.Invalid) : await _tickets.CheckAsync(ticket);
            _done.Enqueue(host => TicketChecked(host, connection, pending, checkedTicket, rejection));
        });
        return true;
    }

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

    public bool IsPlaying(long characterId) => _holders.ContainsKey(characterId);

    public void Update(IJoinHost<TConnection, TCharacter> host)
    {
        while (_done.TryDequeue(out var work))
        {
            work(host);
        }
    }

    public bool TryGet(TConnection connection, out long characterId, out long accountId)
    {
        var found = _joined.TryGetValue(connection, out var binding);
        characterId = found ? binding!.CharacterId : 0;
        accountId = found ? binding!.AccountId : 0;
        return found;
    }

    public void SaveStatus(TConnection connection, CharacterPosition position)
    {
        if (_joined.TryGetValue(connection, out var binding))
        {
            _repository.SaveStatus(binding.CharacterId, binding.Version, position);
        }
    }

    public void SaveCharacter(TConnection connection, TCharacter character)
    {
        if (_joined.TryGetValue(connection, out var binding) && binding.CharacterId == character.Id)
        {
            _repository.SaveCharacter(character, binding.Version);
        }
    }

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

    public Task LeaveAll(Func<TConnection, CharacterPosition?> positionOf) =>
        Task.WhenAll(_joined.Keys.ToList().Select(connection => Leave(connection, positionOf(connection))));

    private void TicketChecked(
        IJoinHost<TConnection, TCharacter> host,
        TConnection connection,
        Pending pending,
        JoinTicket? ticket,
        TicketRejection? rejection
    )
    {
        if (pending.Abandoned)
        {
            return;
        }

        if (ticket == null)
        {
            _pending.Remove(connection);
            host.JoinFailed(
                connection,
                rejection switch
                {
                    TicketRejection.Expired => JoinFailure.ExpiredTicket,
                    TicketRejection.Used => JoinFailure.UsedTicket,
                    TicketRejection.WrongServer => JoinFailure.WrongServer,
                    _ => JoinFailure.BadTicket,
                }
            );
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

    private void Claimed(
        IJoinHost<TConnection, TCharacter> host,
        TConnection connection,
        Pending pending,
        long characterId,
        long accountId,
        CharacterClaim<TCharacter> claim
    )
    {
        if (pending.Abandoned)
        {
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
        if (
            _holders.TryGetValue(refused.CharacterId, out var connection)
            && _joined.TryGetValue(connection, out var binding)
            && binding.Version == refused.Version
        )
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
