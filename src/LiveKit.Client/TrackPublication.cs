namespace LiveKit
{
    /// <summary>
    /// Represents a published track in a LiveKit room.
    /// </summary>
    public class TrackPublication
    {
        internal TrackPublication(ulong asyncId)
        {
            AsyncId = asyncId;
        }

        public ulong AsyncId { get; }
    }
}
