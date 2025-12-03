namespace LiveKit
{
    /// <summary>
    /// Represents a published track in a LiveKit room.
    /// </summary>
    public class TrackPublication
    {
        internal TrackPublication(ulong asyncId, string sid)
        {
            AsyncId = asyncId;
            Sid = sid;
        }

        public ulong AsyncId { get; }
        public string Sid { get; }
    }
}
