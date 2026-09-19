namespace TiktokStudioSandbox.Core.Models;

public enum InstanceLaunchMode
{
    DirectProfile = 1,
    DefaultProfile = 2
}

public enum InstanceStatus
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Error = 3
}

public enum ProxyProtocol
{
    Socks5 = 0,
    Https = 1,
    Http = 2
}

public enum RuleTriggerType
{
    ChatKeyword = 0,
    GiftMinDiamonds = 1,
    MemberJoin = 2,
    LikeMilestone = 3
}


public enum CaptchaType
{
    None = 0,
    Jigsaw = 1,                 // Slide puzzle with draggable knob
    Symbol = 2,                 // Click 2 matching 3D symbols / icons
    Rotate = 3,                 // Rotate image to correct angle
    InactivityModalWaiting = 4, // Inactivity warning modal is open, but SecSDK widget is still rendering
    Unknown = 99
}

public enum CaptchaState
{
    NotPresent = 0,             // No captcha elements in DOM
    HiddenOrUnmounted = 1,      // Found in DOM but hidden (display: none or exited modal)
    ActiveVisible = 2           // Actively displayed and visible on screen
}

public enum CaptchaSolverType
{
    LocalApiServer = 0,         // Call live Python/Node.js ONNX REST API (SimpleSolveCaptchaProject)
    MockResponse = 1            // Use local mock JSON file for testing
}

