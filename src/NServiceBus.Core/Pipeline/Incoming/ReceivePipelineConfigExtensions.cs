﻿namespace NServiceBus
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pipeline;

    /// <summary>
    /// Provides access to receive pipeline notifications.
    /// </summary>
    public static partial class ReceivePipelineConfigExtensions
    {
        /// <summary>
        /// Subscribes to notifications for completed receive pipelines.
        /// </summary>
        public static void OnReceivePipelineCompleted(this PipelineSettings pipelineSettings, Func<ReceivePipelineCompleted, CancellationToken, Task> subscription)
        {
            Guard.ThrowIfNull(pipelineSettings);
            Guard.ThrowIfNull(subscription);

            pipelineSettings.Settings.Get<ReceiveComponent.Settings>().PipelineCompletedSubscribers.Subscribe(subscription);
        }
    }
}