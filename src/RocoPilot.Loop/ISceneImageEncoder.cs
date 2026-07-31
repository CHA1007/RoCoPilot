namespace RocoPilot.Loop;

public interface ISceneImageEncoder
{
    byte[] EncodeKeyframe(FrameSnapshot frame);

    byte[] EncodeOverlay(FrameSnapshot frame);
}
