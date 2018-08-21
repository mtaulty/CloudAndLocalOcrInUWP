namespace OcrScanning
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.Data.Json;
    using Windows.Foundation;
    using Windows.Graphics.Imaging;
    using Windows.Media.Capture;
    using Windows.Media.Capture.Frames;
    using Windows.Media.MediaProperties;
    using Windows.Media.Ocr;
    using Windows.Storage.Streams;
    using Windows.Web.Http;

    public class DeviceAndCloudOcrScanner
    {
        public DeviceAndCloudOcrScanner(MediaFrameSourceFinder mediaFrameSourceFinder,
            Regex regularExpression)
        {
            this.mediaFrameSourceFinder = mediaFrameSourceFinder;
            this.matchExpression = regularExpression;
        }
        /// <summary>
        /// We scan frames from the device, running OCR and trying to find text that matches the pattern 
        /// passed until the point that timeout expires.
        /// We also try and store the 'best' frame of text that we have seen in case we
        /// need to later try to submit to the cloud.
        /// </summary>
        /// <param name="searchPattern"></param>
        /// What to look for.
        /// <param name="timeout"></param>
        /// How long to spend on it.
        /// <returns></returns>
        public async Task<DeviceOcrResult> MatchOnDeviceAsync(TimeSpan timeout)
        {
            var deviceOcrResult = new DeviceOcrResult()
            {
                ResultType = OcrMatchResult.TimeOutNoCloudCallAvailable
            };

            using (var mediaCapture = new MediaCapture())
            {
                await mediaCapture.InitializeAsync(
                    new MediaCaptureInitializationSettings()
                    {
                        StreamingCaptureMode = StreamingCaptureMode.Video,
                        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                        SourceGroup = this.mediaFrameSourceFinder.FrameSourceGroup
                    }
                );
                var frameSource = mediaCapture.FrameSources.Single(
                    fs => fs.Value.Info.Id == this.mediaFrameSourceFinder.FrameSourceInfo.Id);

                // BGRA8 here is intended to line up with what I think the OCR engine supports.
                using (var reader = await mediaCapture.CreateFrameReaderAsync(frameSource.Value,
                    MediaEncodingSubtypes.Bgra8))
                {
                    TaskCompletionSource<bool> completedTask = new TaskCompletionSource<bool>();
                    var timeoutTask = Task.Delay(timeout);

                    var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

                    reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

                    int occupiedFlag = 0;

                    TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs> handler =
                        async (s, e) =>
                        {
                            if (Interlocked.CompareExchange(ref occupiedFlag, 1, 0) == 0)
                            {
                                await OcrProcessFrameAsync(
                                    reader, ocrEngine, deviceOcrResult);

                                if (deviceOcrResult.ResultType == OcrMatchResult.Succeeded)
                                {
                                    completedTask.SetResult(true);
                                }
                                Interlocked.Exchange(ref occupiedFlag, 0);
                            }
                        };

                    reader.FrameArrived += handler;

                    await reader.StartAsync();

                    var timedOut = (await Task.WhenAny(completedTask.Task, timeoutTask)) == timeoutTask;

                    reader.FrameArrived -= handler;

                    await reader.StopAsync();
                }
            }
            return (deviceOcrResult);
        }
        public async Task<GeneralOcrResult> MatchOnCloudAsync(
            string cloudEndpoint,
            string cloudApiKey,
            DeviceOcrResult deviceOcrResult,
            TimeSpan timeBetweenCalls,
            TimeSpan timeout)
        {
            GeneralOcrResult ocrResult = new GeneralOcrResult()
            {
                ResultType = OcrMatchResult.CloudCallFailed
            };

            if (deviceOcrResult.ResultType != OcrMatchResult.TimedOutCloudCallAvailable)
            {
                throw new InvalidOperationException();
            }
            var resultLocation = await SubmitBitmapToCloudGetResultLocationAsync(
                cloudEndpoint, cloudApiKey, deviceOcrResult);

            if (!string.IsNullOrEmpty(resultLocation))
            {
                ocrResult = await PollCloudResultLocation(resultLocation, cloudApiKey, timeBetweenCalls, timeout);
            }
            return (ocrResult);
        }
        async Task<GeneralOcrResult> PollCloudResultLocation(
            string operationLocationUri,
            string cloudApiKey,
            TimeSpan timeBetweenCalls,
            TimeSpan timeout)
        {
            GeneralOcrResult result = new GeneralOcrResult()
            {
                ResultType = OcrMatchResult.CloudCallTimedOut
            };
            var delayTask = Task.Delay(timeout);

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders[API_SUBSCRIPTION_KEY_HEADER] = cloudApiKey;
                bool continuePolling = true;

                while (!delayTask.IsCompleted && continuePolling)
                {
                    // Note - some sort of delay between polling calls might be nice? :-)
                    var response = await httpClient.GetAsync(new Uri(operationLocationUri));

                    // Note that this API can return 'too many requests' and ask you to re-call
                    // in a number of seconds. Not sure what to do on that front.
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var jsonObject = JsonObject.Parse(jsonResponse);

                        switch (jsonObject[JSON_STATUS_VALUE].GetString())
                        {
                            case RECOGNITION_API_SUCCEEDED:
                                continuePolling = false;
                                result.MatchedText = this.ParseRecognitionServiceResponseForMatch(jsonObject);
                                result.ResultType = 
                                    string.IsNullOrEmpty(result.MatchedText) ? OcrMatchResult.CloudCallProducedNoMatch : OcrMatchResult.Succeeded;
                                break;
                            case RECOGNITION_API_RUNNING:
                            case RECOGNITION_API_NOT_STARTED:
                                break;
                            case RECOGNITION_API_FAILED:
                                result.ResultType = OcrMatchResult.CloudCallFailed;
                                break;
                            default:
                                break;
                        }
                    }
                    await Task.Delay(timeBetweenCalls);
                }
            }
            return (result);
        }
        string ParseRecognitionServiceResponseForMatch(JsonObject jsonResponse)
        {
            string result = null;

            var lines =
                  jsonResponse?[JSON_RECOGNITION_RESULT_VALUE].GetObject()?[JSON_LINES_VALUE]?.GetArray();

            var textLines = lines?.Select(l => l?.GetObject()?[JSON_TEXT_VALUE]?.GetString());

            if (textLines != null)
            {
                foreach (var textLine in textLines)
                {
                    var matches = this.matchExpression.Matches(textLine);

                    if (matches?.Count > 0)
                    {
                        result = matches[0].Value;
                        break;
                    }
                }
            }
            return (result);
        }
        static async Task<string> SubmitBitmapToCloudGetResultLocationAsync(
            string cloudEndpoint,
            string cloudApiKey,
            DeviceOcrResult deviceOcrResult)
        {
            string resultLocation = null;

            // First, encode the software bitmap as a Jpeg...
            using (var memoryStream = new InMemoryRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memoryStream);
                encoder.SetSoftwareBitmap(deviceOcrResult.BestOcrSoftwareBitmap);
                await encoder.FlushAsync();
                memoryStream.Seek(0);

                // Now, send it off to the computer vision API.
                using (var httpClient = new HttpClient())
                using (var httpContent = new HttpStreamContent(memoryStream))
                {
                    httpContent.Headers["Content-Type"] = "application/octet-stream";
                    httpClient.DefaultRequestHeaders[API_SUBSCRIPTION_KEY_HEADER] = cloudApiKey;

                    using (var response = await httpClient.PostAsync(new Uri(cloudEndpoint), httpContent))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            resultLocation = response.Headers["Operation-Location"];
                        }
                    }
                }
            }
            return (resultLocation);
        }
        async Task OcrProcessFrameAsync(
            MediaFrameReader reader,
            OcrEngine ocrEngine,
            DeviceOcrResult ocrDeviceResult)
        {
            using (var frame = reader.TryAcquireLatestFrame())
            {
                if (frame?.VideoMediaFrame != null)
                {
                    using (var bitmap = frame.VideoMediaFrame.SoftwareBitmap)
                    {
                        var result = await ocrEngine.RecognizeAsync(bitmap);

                        if (result?.Text != null)
                        {
                            var matchingResults = this.matchExpression.Matches(result.Text);

                            var matched = matchingResults?.Count > 0;

                            if (matched)
                            {
                                // We take the first one, we don't do multiple (yet).
                                ocrDeviceResult.MatchedText = matchingResults[0].Value;
                                ocrDeviceResult.ResultType = OcrMatchResult.Succeeded;
                                ocrDeviceResult.BestOcrSoftwareBitmap?.Dispose();
                                ocrDeviceResult.BestOcrSoftwareBitmap = null;
                            }
                            else if (result.Text.Length > ocrDeviceResult.BestOcrTextLengthFound)
                            {
                                ocrDeviceResult.BestOcrTextLengthFound = result.Text.Length;
                                ocrDeviceResult.BestOcrSoftwareBitmap?.Dispose();
                                ocrDeviceResult.BestOcrSoftwareBitmap = SoftwareBitmap.Copy(bitmap);
                                ocrDeviceResult.ResultType = OcrMatchResult.TimedOutCloudCallAvailable;
                            }
                        }
                    }
                }
            }
        }
        MediaFrameSourceFinder mediaFrameSourceFinder;
        Regex matchExpression;
        static readonly string API_SUBSCRIPTION_KEY_HEADER = "Ocp-Apim-Subscription-Key";
        const string RECOGNITION_API_SUCCEEDED = "Succeeded";
        const string RECOGNITION_API_FAILED = "Failed";
        const string RECOGNITION_API_NOT_STARTED = "Not started";
        const string RECOGNITION_API_RUNNING = "Running";

        static readonly string JSON_STATUS_VALUE = "status";
        static readonly string JSON_LINES_VALUE = "lines";
        static readonly string JSON_TEXT_VALUE = "text";
        static readonly string JSON_RECOGNITION_RESULT_VALUE = "recognitionResult";
    }
}