#nullable enable

namespace NServiceBus
{
    using Faults;

    /// <summary>
    /// Notifications.
    /// </summary>
    public class Notifications
    {
        /// <summary>
        /// Push-based error notifications.
        /// </summary>
        public ErrorsNotifications Errors { get; } = new ErrorsNotifications();
    }
}