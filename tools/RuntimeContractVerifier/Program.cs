using InteractiveWallpaper;

var bounds = new VirtualDesktopBounds { x = -1920, y = 0, width = 4480, height = 1440 };
var center = DesktopCoordinateMath.PixelToWorld(320, 720, bounds, 10);
if (Math.Abs(center.X) > 0.0001 || Math.Abs(center.Y) > 0.0001)
{
    throw new InvalidOperationException("Virtual desktop center did not map to world origin.");
}
var topLeft = DesktopCoordinateMath.PixelToWorld(-1920, 0, bounds, 10);
if (topLeft.X >= 0 || topLeft.Y <= 0)
{
    throw new InvalidOperationException("Virtual desktop orientation is incorrect.");
}

var state = new ProxyIconStateMachine();
if (!state.TryTransition(ProxyIconMotionState.UserDragging) ||
    !state.TryTransition(ProxyIconMotionState.DesktopPinned) ||
    !state.TryTransition(ProxyIconMotionState.CharacterGrabbed) ||
    !state.TryTransition(ProxyIconMotionState.Dynamic) ||
    state.TryTransition(ProxyIconMotionState.UserDragging) == false)
{
    throw new InvalidOperationException("Proxy icon state machine rejected a valid transition sequence.");
}

var attachment = new DesktopWindowAttachment(true, true, true, 4480, 1440);
if (!attachment.Attached || attachment.Width != 4480 || attachment.Height != 1440)
{
    throw new InvalidOperationException("Native desktop window attachment contract is invalid.");
}

Console.WriteLine("Managed desktop coordinate, proxy state, and native attachment contracts verified.");