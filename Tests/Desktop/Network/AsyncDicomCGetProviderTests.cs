using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Dicom.Helpers;
using Dicom.Log;
using Xunit;
using Xunit.Abstractions;

namespace Dicom.Network
{
    public class AsyncDicomCGetProviderTests
    {
        private readonly XUnitDicomLogger _logger;

        public AsyncDicomCGetProviderTests(ITestOutputHelper testOutputHelper)
        {
            _logger = new XUnitDicomLogger(testOutputHelper)
                .IncludeTimestamps()
                .IncludeThreadId()
                .WithMinimumLevel(LogLevel.Debug);
        }

        [Fact]
        public async Task OnCGetRequestAsync_ImmediateSuccess_ShouldRespond()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<ImmediateSuccessAsyncDicomCGetProvider>(port, logger: _logger.IncludePrefix("DicomServer")))
            {
                var client = new Network.Client.DicomClient("127.0.0.1", port, false, "SCU", "ANY-SCP")
                {
                    Logger = _logger.IncludePrefix(typeof(DicomClient).Name)
                };

                DicomCGetResponse response = null;
                DicomRequest.OnTimeoutEventArgs timeout = null;
                var studyInstanceUID = DicomUIDGenerator.GenerateDerivedFromUUID().ToString();
                var request = new DicomCGetRequest(studyInstanceUID)
                {
                    OnResponseReceived = (req, res) => response = res,
                    OnTimeout = (sender, args) => timeout = args
                };

                await client.AddRequestAsync(request).ConfigureAwait(false);
                await client.SendAsync().ConfigureAwait(false);

                Assert.NotNull(response);
                Assert.Equal(DicomStatus.Success, response.Status);
                Assert.Null(timeout);
            }
        }

        [Fact]
        public async Task OnCGetRequestAsync_Pending_ShouldRespond()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<PendingAsyncDicomCGetProvider>(port, logger: _logger.IncludePrefix("DicomServer")))
            {
                var client = new Network.Client.DicomClient("127.0.0.1", port, false, "SCU", "ANY-SCP")
                {
                    Logger = _logger.IncludePrefix(typeof(DicomClient).Name)
                };

                var responses = new ConcurrentQueue<DicomCGetResponse>();
                DicomRequest.OnTimeoutEventArgs timeout = null;
                var studyInstanceUID = DicomUIDGenerator.GenerateDerivedFromUUID().ToString();
                var request = new DicomCGetRequest(studyInstanceUID)
                {
                    OnResponseReceived = (req, res) => responses.Enqueue(res),
                    OnTimeout = (sender, args) => timeout = args
                };

                await client.AddRequestAsync(request).ConfigureAwait(false);
                await client.SendAsync().ConfigureAwait(false);

                Assert.Collection(
                    responses,
                    response1 => Assert.Equal(DicomStatus.Pending, response1.Status),
                    response2 => Assert.Equal(DicomStatus.Pending, response2.Status),
                    response3 => Assert.Equal(DicomStatus.Success, response3.Status)
                );
                Assert.Null(timeout);
            }
        }
    }

    #region helper classes

    public class ImmediateSuccessAsyncDicomCGetProvider : DicomService, IDicomServiceProvider, IAsyncDicomCGetProvider
    {
        public ImmediateSuccessAsyncDicomCGetProvider(INetworkStream stream, Encoding fallbackEncoding, Logger log)
            : base(stream, fallbackEncoding, log)
        {
        }

        async Task WaitForALittleBit()
        {
            var ms = new Random().Next(10);
            await Task.Delay(ms).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            await WaitForALittleBit().ConfigureAwait(false);

            foreach (var pc in association.PresentationContexts)
            {
                pc.SetResult(DicomPresentationContextResult.Accept);
            }

            await SendAssociationAcceptAsync(association);
        }

        /// <inheritdoc />
        public async Task OnReceiveAssociationReleaseRequestAsync()
        {
            await SendAssociationReleaseResponseAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        {
        }

        /// <inheritdoc />
        public void OnConnectionClosed(Exception exception)
        {
        }

        public async Task<IEnumerable<Task<DicomCGetResponse>>> OnCGetRequestAsync(DicomCGetRequest request)
        {
            await WaitForALittleBit().ConfigureAwait(false);

            return InnerOnCGetRequestAsync();

            IEnumerable<Task<DicomCGetResponse>> InnerOnCGetRequestAsync()
            {
                yield return Task.FromResult(new DicomCGetResponse(request, DicomStatus.Success));
            }
        }
    }

    public class PendingAsyncDicomCGetProvider : DicomService, IDicomServiceProvider, IAsyncDicomCGetProvider
    {
        public PendingAsyncDicomCGetProvider(INetworkStream stream, Encoding fallbackEncoding, Logger log)
            : base(stream, fallbackEncoding, log)
        {
        }

        async Task WaitForALittleBit()
        {
            var ms = new Random().Next(10);
            await Task.Delay(ms).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            await WaitForALittleBit().ConfigureAwait(false);

            foreach (var pc in association.PresentationContexts)
            {
                pc.SetResult(DicomPresentationContextResult.Accept);
            }

            await SendAssociationAcceptAsync(association);
        }

        /// <inheritdoc />
        public async Task OnReceiveAssociationReleaseRequestAsync()
        {
            await SendAssociationReleaseResponseAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        {
        }

        /// <inheritdoc />
        public void OnConnectionClosed(Exception exception)
        {
        }

        public async Task<IEnumerable<Task<DicomCGetResponse>>> OnCGetRequestAsync(DicomCGetRequest request)
        {
            await WaitForALittleBit().ConfigureAwait(false);

            return InnerOnCGetRequestAsync();

            IEnumerable<Task<DicomCGetResponse>> InnerOnCGetRequestAsync()
            {
                yield return Task.FromResult(new DicomCGetResponse(request, DicomStatus.Pending));
                yield return Task.FromResult(new DicomCGetResponse(request, DicomStatus.Pending));
                yield return Task.FromResult(new DicomCGetResponse(request, DicomStatus.Success));
            }
        }
    }


    #endregion
}
