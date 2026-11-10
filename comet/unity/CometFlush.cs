using UnityEngine;

namespace Comet.Unity
{
    /// <summary>Added by the connection; a separate component because one script has only one execution order.</summary>
    [DefaultExecutionOrder(int.MaxValue)]
    [RequireComponent(typeof(CometConnection))]
    public sealed class CometFlush : MonoBehaviour
    {
        private CometConnection _connection = null!;

        private void Awake() => _connection = GetComponent<CometConnection>();

        private void LateUpdate() => _connection.Session?.Flush(CometConnection.Now);
    }
}
