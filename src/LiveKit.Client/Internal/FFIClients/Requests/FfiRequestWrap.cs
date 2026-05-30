using System;
using Google.Protobuf;
using LiveKit.Internal.FFIClients.Pools;
using LiveKit.Proto;
using LiveKit.Internal;

namespace LiveKit.Internal.FFIClients.Requests
{
    /// <summary>
    /// Standardized request packaging helper that handles the
    /// RegisterPendingCallback / SendRequest / CancelPendingCallback workflow.
    /// </summary>
    public struct FfiRequestWrap<T> : IDisposable where T : class, new()
    {
        public readonly T request;
        public ulong RequestAsyncId { get; }
        private readonly IMultiPool _multiPool;
        private readonly IFFIClient _ffiClient;
        private readonly FfiRequest _ffiRequest;
        private readonly Action<FfiRequest> _releaseFfiRequest;
        private readonly Action<T> _releaseRequest;

        private bool _sent;

        public FfiRequestWrap(IFFIClient ffiClient, IMultiPool multiPool) : this(
            multiPool.Get<T>(),
            multiPool,
            multiPool.Get<FfiRequest>(),
            ffiClient,
            multiPool.Release,
            multiPool.Release
        )
        {
        }

        public FfiRequestWrap(
            T request,
            IMultiPool multiPool,
            FfiRequest ffiRequest,
            IFFIClient ffiClient,
            Action<FfiRequest> releaseFfiRequest,
            Action<T> releaseRequest
        )
        {
            this.request = request;
            _multiPool = multiPool;
            _ffiRequest = ffiRequest;
            _ffiClient = ffiClient;
            _releaseFfiRequest = releaseFfiRequest;
            _releaseRequest = releaseRequest;
            RequestAsyncId = request.InitializeRequestAsyncId();
            _sent = false;
        }

        public FfiResponseWrap Send()
        {
            if (_sent)
                throw new InvalidOperationException("Request already sent");

            _sent = true;
            _ffiRequest.Inject(request);
            try
            {
                var response = _ffiClient.SendRequest(_ffiRequest);
                return new FfiResponseWrap(response, _ffiClient);
            }
            catch
            {
                if (RequestAsyncId != 0 && _ffiClient is LiveKit.Internal.FfiClient client)
                {
                    client.CancelPendingCallback(RequestAsyncId);
                }
                throw;
            }
        }

        public SmartWrap<TK> TempResource<TK>() where TK : class, IMessage, new()
        {
            var resource = _multiPool.Get<TK>();
            return new SmartWrap<TK>(resource, _multiPool);
        }

        public void Dispose()
        {
            _ffiRequest.ClearMessage();
            _releaseRequest(request);
            _releaseFfiRequest(_ffiRequest);
        }
    }
}
