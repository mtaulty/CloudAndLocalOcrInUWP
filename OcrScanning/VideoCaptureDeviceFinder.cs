namespace OcrScanning
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Windows.Devices.Enumeration;

    public class VideoCaptureDeviceFinder
    {
        public bool HasDevice => this.device != null;

        internal DeviceInformation Device
        {
            get
            {
                if (this.device == null)
                {
                    throw new InvalidOperationException();
                }
                return (this.device);
            }
        }

        public static DeviceInformation FirstFilter(DeviceInformationCollection collection) 
            => collection.FirstOrDefault();

        public static DeviceInformation DefaultDeviceFilter(DeviceInformationCollection collection)
            => collection.FirstOrDefault(d => d.IsDefault);

        public static DeviceInformation FirstEnabledFilter(DeviceInformationCollection collection) =>
            collection.FirstOrDefault(d => d.IsEnabled);

        static async Task<DeviceInformation> FindAsync(
          Func<DeviceInformationCollection, DeviceInformation> filter)
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            return (filter(devices));
        }
        public async Task InitialiseAsync(
            Func<DeviceInformationCollection, DeviceInformation> filter)
        {
            this.device = await FindAsync(filter);
        }
        public DeviceInformation device;
    }
}
