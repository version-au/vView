using vView.Core;
using Xunit;

namespace vView.Tests
{
    public class VlcLogTests
    {
        [Theory]
        [InlineData("Error", "main", "failed to create audio output")]
        [InlineData("Error", "main", "module not functional")]
        [InlineData("Error", "main", "Could not convert timestamp 0 for FFmpeg")]
        [InlineData("Error", "main", "Timestamp conversion failed for 1919001: no reference clock")]
        [InlineData("Error", "main", "ES_OUT_SET_(GROUP_)PCR  is called too late (pts_delay increased to 1000 ms)")]
        [InlineData("Error", "avcodec", "avcodec_send_packet critical error")]
        [InlineData("Error", "gnutls", "The specified session has been invalidated for some reason.")]
        [InlineData("Error", "gnutls", "Error decoding the received TLS packet.")]
        [InlineData("Error", "main", "SetThumbNailClip failed")]
        public void RoutineMessagesAreLeftOut(string level, string module, string message)
        {
            Assert.False(VlcLog.WorthLogging(level, module, message));
        }

        [Theory]
        [InlineData("Error", "avcodec", "Failed to open rtmp://example:1935/live/x: Input/output error")]
        [InlineData("Error", "main", "Your input can't be opened")]
        [InlineData("Warning", "http", "cannot connect to example:80")]
        [InlineData("Warning", "adaptive", "Encountered an unsupported playlist")]
        public void RealProblemsAreKept(string level, string module, string message)
        {
            Assert.True(VlcLog.WorthLogging(level, module, message));
        }

        [Theory]
        [InlineData("Warning", "main", "picture is too late to be displayed")]
        [InlineData("Debug", "http", "anything")]
        [InlineData("Notice", "main", "anything")]
        public void QuietLevelsAreLeftOut(string level, string module, string message)
        {
            Assert.False(VlcLog.WorthLogging(level, module, message));
        }
    }
}
