using System;
using System.Runtime.InteropServices;
using System.Threading;
using Auralis8D.Audio.Interop.WASAPI;

namespace Auralis8D.Audio.Capture
{
    /// <summary>
    /// Managed COM completion handler for ActivateAudioInterfaceAsync.
    /// Signals when Windows finishes asynchronous activation of the requested audio interface.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class ActivateAudioCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEventSlim _completionEvent = new ManualResetEventSlim(false);

        /// <summary>
        /// The HRESULT result of the interface activation.
        /// </summary>
        public int ActivationResult { get; private set; } = -1;

        /// <summary>
        /// The COM interface returned by Windows upon successful activation.
        /// </summary>
        public object? ActivatedInterface { get; private set; }

        /// <summary>
        /// Synchronously waits for the asynchronous activation to complete.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>True if activation completed before timeout, false otherwise.</returns>
        public bool WaitForCompletion(int timeoutMs = 3000)
        {
            return _completionEvent.Wait(timeoutMs);
        }

        /// <summary>
        /// Callback invoked by the Windows OS audio subsystem when activation is ready.
        /// </summary>
        /// <param name="activateOperation">Async operation object containing activation status and interface.</param>
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                if (activateOperation != null)
                {
                    int hr = activateOperation.GetActivateResult(out int actResult, out object actInterface);
                    if (hr == 0)
                    {
                        ActivationResult = actResult;
                        ActivatedInterface = actInterface;
                    }
                    else
                    {
                        ActivationResult = hr;
                    }
                }
            }
            catch (Exception ex)
            {
                ActivationResult = Marshal.GetHRForException(ex);
            }
            finally
            {
                _completionEvent.Set();
            }
            return 0; // S_OK
        }
    }
}
