using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Sends what a frame wrote to its <see cref="CometConnection"/>'s session, after every other script's
    /// LateUpdate. Added by the connection; a separate component because one script has one execution order.
    /// </summary>
    [DefaultExecutionOrder(int.MaxValue)]
    [RequireComponent(typeof(CometConnection))]
    public sealed class CometFlush : MonoBehaviour
    {
        private CometConnection _connection = null!;

        private void Awake() => _connection = GetComponent<CometConnection>();

        private void LateUpdate() => _connection.Session?.Flush(CometConnection.Now);
    }
}
