namespace BackgroundResourceProcessing
{
    /// <summary>
    /// The event fired when a changepoint occurs.
    /// </summary>
    public struct ChangepointEvent
    {
        public double LastChangepoint;
        public double CurrentChangepoint;
    }
}
