namespace OcrScanning
{
    public enum OcrMatchResult
    {
        Succeeded,
        TimedOutCloudCallAvailable,
        TimeOutNoCloudCallAvailable,
        CloudCallFailed,
        CloudCallTimedOut,
        CloudCallProducedNoMatch
    }
}