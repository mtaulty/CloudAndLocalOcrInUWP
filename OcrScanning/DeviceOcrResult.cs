namespace OcrScanning
{
    using System;
    using Windows.Graphics.Imaging;

    public class DeviceOcrResult : GeneralOcrResult, IDisposable
    {
        internal DeviceOcrResult()
        {

        }
        ~DeviceOcrResult()
        {
            this.Dispose(false);
        }
        internal SoftwareBitmap BestOcrSoftwareBitmap { get; set; }
        internal int BestOcrTextLengthFound { get; set; }

        public void Dispose()
        {
            this.Dispose(true);
        }
        void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.BestOcrSoftwareBitmap?.Dispose();
            }
        }
    }
}