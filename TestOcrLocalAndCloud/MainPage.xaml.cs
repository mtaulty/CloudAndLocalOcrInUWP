#define HOLOLENS
namespace TestOcrLocalAndCloud
{
    using OcrScanning;
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using static OcrScanning.MediaFrameSourceFinder;

    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }
        async void OnStartOcrAsync(object sender, RoutedEventArgs e)
        {
            this.txtResult.Text = "Starting...";

            var videoCaptureDeviceFinder = new VideoCaptureDeviceFinder();

#if HOLOLENS
            await videoCaptureDeviceFinder.InitialiseAsync(
                devices => devices.First());
#else
            await videoCaptureDeviceFinder.InitialiseAsync(
                devices => devices.Where(device => device.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front).Single());
#endif // HOLOLENS

            if (videoCaptureDeviceFinder.HasDevice)
            {
                var mediaFrameSourceFinder = new MediaFrameSourceFinder(videoCaptureDeviceFinder);

#if HOLOLENS
                var filters = new FilterSet()
                    .Append(MediaFrameSourceFinder.VideoPreviewFilter, MediaFrameSourceFinder.ColorFilter)
                    .Append(m => ((m.Width == 1280) && (m.FrameRate >= 30)));
#else
                var filters = new FilterSet()
                    .Append(MediaFrameSourceFinder.VideoPreviewFilter, MediaFrameSourceFinder.ColorFilter)
                    .Append(m => ((m.Width == 1920) && (m.FrameRate >= 30)));
#endif // HOLOLENS

                await mediaFrameSourceFinder.InitialiseAsync(
                    matchingMediaSourceInfos => matchingMediaSourceInfos.First(), filters);

                var ocrScanner = new DeviceAndCloudOcrScanner(
                    mediaFrameSourceFinder, new Regex(IP_ADDRESS_REGEX));

                this.txtResult.Text = "Matching with the local device camera...";

                using (var matchedResult = await ocrScanner.MatchOnDeviceAsync(TimeSpan.FromSeconds(10)))
                {
                    if (matchedResult.ResultType == OcrMatchResult.Succeeded)
                    {
                        // We found what we were looking for, finished!
                        this.txtResult.Text = $"Found result {matchedResult.MatchedText}";
                    }
                    else if (matchedResult.ResultType == OcrMatchResult.TimedOutCloudCallAvailable)
                    {
                        // We haven't matched but we have got a frame with at least some text in
                        // it which the on-device OCR hasn't matched.
                        // So...we can try the cloud.
                        this.txtResult.Text = $"Calling cloud...";

                        var result = await ocrScanner.MatchOnCloudAsync(
                            AZURE_TEXT_RECOGNITION_API,
                            AZURE_COMPUTER_VISION_KEY,
                            matchedResult,
                            TimeSpan.FromSeconds(5),
                            TimeSpan.FromSeconds(30));

                        if (result.ResultType == OcrMatchResult.Succeeded)
                        {
                            this.txtResult.Text = $"Found result {result.MatchedText}";
                        }
                        else
                        {
                            this.txtResult.Text = $"Didn't work {result.ResultType}";
                        }
                    }
                    else
                    {
                        this.txtResult.Text = $"Result returned {matchedResult.ResultType}";
                    }
                }
            }
        }
#error "Add you Azure Computer Vision Key Here"
        static readonly string AZURE_COMPUTER_VISION_KEY = string.Empty;

        static readonly string AZURE_TEXT_RECOGNITION_API = 
            "https://westeurope.api.cognitive.microsoft.com/vision/v2.0/recognizeText?mode=Printed";

        static readonly string IP_ADDRESS_REGEX =
            @"(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}";
    }
}
