namespace OcrScanning
{
    public class GeneralOcrResult
    {
        internal GeneralOcrResult()
        {

        }
        public OcrMatchResult ResultType { get; internal set; }
        public string MatchedText { get; internal set; }
    }
}