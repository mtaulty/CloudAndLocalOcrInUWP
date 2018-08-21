namespace OcrScanning
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Windows.Media.Capture;
    using Windows.Media.Capture.Frames;

    public class MediaFrameSourceFinder
    {
        public class FilterSet
        {
            public FilterSet()
            {
                this.mediaFrameSourceFilters = new List<Predicate<MediaFrameSourceInfo>>();
                this.videoProfileFilters = new List<Predicate<MediaCaptureVideoProfileMediaDescription>>();
            }
            public FilterSet Append(params Predicate<MediaFrameSourceInfo>[] predicates)
            {
                this.mediaFrameSourceFilters.AddRange(predicates);
                return (this);
            }
            public FilterSet Append(params Predicate<MediaCaptureVideoProfileMediaDescription>[] predicates)
            {
                this.videoProfileFilters.AddRange(predicates);
                return (this);
            }
            internal bool All(MediaFrameSourceInfo sourceInfo) => 
                this.mediaFrameSourceFilters.All(p => p(sourceInfo));

            internal bool All(MediaCaptureVideoProfileMediaDescription description) =>
                this.videoProfileFilters.All(p => p(description));
                
            List<Predicate<MediaFrameSourceInfo>> mediaFrameSourceFilters;
            List<Predicate<MediaCaptureVideoProfileMediaDescription>> videoProfileFilters;
        }
        
        public MediaFrameSourceFinder(VideoCaptureDeviceFinder deviceFinder)
        {
            this.deviceFinder = deviceFinder;
        }
        public bool HasSourceInfo => this.mediaFrameSourceInfo != null;

        internal MediaFrameSourceGroup FrameSourceGroup =>
            this.FrameSourceInfo.SourceGroup;

        internal MediaFrameSourceInfo FrameSourceInfo
        {
            get
            {
                if (this.mediaFrameSourceInfo == null)
                {
                    throw new InvalidOperationException();
                }
                return (this.mediaFrameSourceInfo);
            }
        }

        public static bool VideoPreviewFilter(MediaFrameSourceInfo sourceInfo) =>
            sourceInfo.MediaStreamType == MediaStreamType.VideoPreview;

        public static bool ColorFilter(MediaFrameSourceInfo sourceInfo) =>
            sourceInfo.SourceKind == MediaFrameSourceKind.Color;

        public async Task InitialiseAsync(
            Func<IEnumerable<MediaFrameSourceInfo>, MediaFrameSourceInfo> sourceInfoSelector,
            FilterSet filterChain = null)
        {
            // Get all the media frame source groups.
            var mediaFrameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();

            // Flatten all this data out so that we have access to the groups, the source infos and the
            // video profiles.
            var mediaSourceInfos = mediaFrameSourceGroups
                .SelectMany(
                    group => group.SourceInfos
                )
                .SelectMany(
                    sourceInfo => sourceInfo.VideoProfileMediaDescription,
                    (sourceInfo, videoProfileDescriptions) =>
                        new
                        {
                            MediaFrameSourceInfo = sourceInfo,
                            VideoProfileDescription = videoProfileDescriptions
                        }
                );

            // Find the ones which match our device & which satisfy all of the media frame source info
            // filters && all of the video device profile filters.
            var filteredSourceInfos = mediaSourceInfos
                .Where(
                    entry => (
                        (entry.MediaFrameSourceInfo.DeviceInformation.Id == this.deviceFinder.Device.Id) &&
                        (filterChain == null || filterChain.All(entry.MediaFrameSourceInfo)) &&
                        (filterChain == null || filterChain.All(entry.VideoProfileDescription))));

            this.mediaFrameSourceInfo =
                sourceInfoSelector(filteredSourceInfos.Select(si => si.MediaFrameSourceInfo));
        }

        MediaFrameSourceInfo mediaFrameSourceInfo;

        VideoCaptureDeviceFinder deviceFinder;
    }
}
