namespace LiveKit
{
    /// <summary>
    /// Represents a published track in a LiveKit room.
    /// </summary>
    public class TrackPublication
    {
        internal TrackPublication(ulong asyncId, LiveKit.Proto.TrackPublicationInfo info)
        {
            AsyncId = asyncId;
            Info = info;
        }

        public ulong AsyncId { get; }
        public LiveKit.Proto.TrackPublicationInfo Info { get; }
    }
}
