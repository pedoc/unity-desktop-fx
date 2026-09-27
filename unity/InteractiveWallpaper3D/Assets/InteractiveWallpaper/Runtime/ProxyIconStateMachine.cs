#nullable enable

namespace InteractiveWallpaper
{
    public enum ProxyIconMotionState
    {
        DesktopPinned,
        UserDragging,
        Dynamic,
        CharacterGrabbed,
    }

    public sealed class ProxyIconStateMachine
    {
        public ProxyIconMotionState State { get; private set; } = ProxyIconMotionState.DesktopPinned;

        public bool TryTransition(ProxyIconMotionState next)
        {
            if (next == State)
            {
                return true;
            }

            var allowed = State switch
            {
                ProxyIconMotionState.DesktopPinned =>
                    next is ProxyIconMotionState.UserDragging or
                        ProxyIconMotionState.Dynamic or
                        ProxyIconMotionState.CharacterGrabbed,
                ProxyIconMotionState.UserDragging =>
                    next is ProxyIconMotionState.DesktopPinned or ProxyIconMotionState.Dynamic,
                ProxyIconMotionState.Dynamic =>
                    next is ProxyIconMotionState.DesktopPinned or
                        ProxyIconMotionState.UserDragging or
                        ProxyIconMotionState.CharacterGrabbed,
                ProxyIconMotionState.CharacterGrabbed =>
                    next is ProxyIconMotionState.DesktopPinned or ProxyIconMotionState.Dynamic,
                _ => false,
            };
            if (allowed)
            {
                State = next;
            }
            return allowed;
        }
    }
}
