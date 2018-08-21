# CloudAndLocalOcrInUWP

This repo contains a simple library and a test app project which try to provide a starting point for a Universal Windows Platform solution to doing Optical Character Recognition locally on a device while providing fallback to the cloud.

The device that I was mainly targeting with this code was the HoloLens where you do not usually want to provide some media-capture preview because the user already sees the real-world around them and they just want to look at a piece of text and have the HoloLens camera automatically recognise it.

With OCR build into the Universal Windows Platform (see [OcrEngine](https://docs.microsoft.com/en-us/uwp/api/Windows.Media.Ocr.OcrEngine)) it seems natural to use this wherever possible because it provides zero-cost, low-latency results. Because of those features, it is possible to open up the camera and run any number of frames past the OCR engine looking for a match while the user moves their viewpoint around. This is great in that the user doesn't have to provide a single-shot "perfect picture" but, instead, the algorithms can run until some timeout or a result is produced.

However, I have found in recent experiments that the [Azure Computer Vision Text Recognition service](https://docs.microsoft.com/en-us/azure/cognitive-services/Computer-vision/quickstarts/csharp-print-text) can produce better results than the on-device capability in some circumstances. However, the cloud service introduces latency and a cost-per-invocation and so typically can't be invoked with images at 10,20,30 frames per second.

This library, then, tries to combine the two approaches. The idea is that a caller might be trying to find some particular piece of (printed) text within an image that can be described by a regular expression (e.g. a postcode or IP address).

The library offers a way to open the camera and scan the resulting frames using the on-device OCR engine to try and find a match for the regular expression.

If the match is found then the process ends. Otherwise, after some timeout (which could be infinite) the code will return reporting that it has not found text to match.

However, at this point the library can also return that it has saved the 'best' frame captured from the camera which could then be sent to the cloud for further analysis and it provides an easy API to make that call.

In this library, 'best' is currently defined as the captured frame which the on-device OCR engine found had the largest quantity of text recognised within it.

In usage,  the caller first instantiates a **VideoCaptureDeviceFinder** and needs to provide a means via which a single video device can be found. For example;

```csharp
var videoCaptureDeviceFinder = new VideoCaptureDeviceFinder();

await videoCaptureDeviceFinder.InitialiseAsync(
devices => devices.Where(
	device => device.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front).Single());
```

the **HasDevice** property can be used to figure out whether a suitable device has been found or not after initialization. 

There's also a need to use a **MediaFrameSourceFinder** which provides a way to filter down all the possible media sources that the device might be capable of providing - an example below;

```csharp
var mediaFrameSourceFinder = new MediaFrameSourceFinder(videoCaptureDeviceFinder);

var filters = new FilterSet()
	.Append(MediaFrameSourceFinder.VideoPreviewFilter, MediaFrameSourceFinder.ColorFilter)
          .Append(m => ((m.Width == 1280) && (m.FrameRate >= 30)));
               
await mediaFrameSourceFinder.InitialiseAsync(matchingMediaSourceInfos => matchingMediaSourceInfos.First(), filters);
```

With these objects created, the main **DeviceAndCloudOcrScanner** can be spun up by passing it the **MediaFrameSourceFinder** and a regular expression to be found in the OCR'd text;

```csharp
var ocrScanner = new DeviceAndCloudOcrScanner(
	mediaFrameSourceFinder, new Regex(IP_ADDRESS_REGEX));
```

and we can ask it to run on the local camera to see if it can find a suitably matching piece of text within a certain timeframe (or forever if you want to use **TimeSpan.FromMilliseconds(-1)**);

```csharp
var matchedResult = await ocrScanner.MatchOnDeviceAsync(TimeSpan.FromSeconds(10));
```

the return value here can report whether a match has been found and what that match is but it can additionally report that no match was found within the timeout and whether a suitable frame (i.e. one with the maximum amount of OCR'd text in it) is available to send on to the cloud as per the example below;

```csharp
if (matchedResult.ResultType == OcrMatchResult.TimedOutCloudCallAvailable)
{
	var result = await ocrScanner.MatchOnCloudAsync(
		AZURE_TEXT_RECOGNITION_API,
		AZURE_COMPUTER_VISION_KEY,
		matchedResult,
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(30));
}
```
The call above is saying "take the best frame that we have from the local OCR engine and send it to the cloud" and we are using a total timeout here of 30 seconds. The Azure Text Recognition API is 2-part API in that it has an endpoint which takes an image and returns a URL.

That URL must then be polled for the result of the OCR operation when it is available. Consequently, the **MatchOnCloudAsync** API above takes both a total timeout (30s) along with a "how frequently to poll for an answer" **TimeSpan** which is 5 seconds here.

Again, that API can then return whether it succeeded or not in calling the cloud and, if it succeeded, whether it further succeeded in getting some OCR'd text which matched the regular expression.

All of this is built into the 2D test app in the repo and I've tried this on both PC and on HoloLens where it seems to work 'reasonably' given it's just a sample that was put together fairly quickly. Note that I'd expect it to work from a 3D app on HoloLens too.