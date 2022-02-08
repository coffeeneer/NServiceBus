﻿namespace NServiceBus.Core.Tests.Recoverability
{
    using System;
    using System.Collections.Generic;
    using NServiceBus.Extensibility;
    using NUnit.Framework;
    using Testing;
    using Transport;

    [TestFixture]
    public class DiscardRecoverabilityActionTests
    {
        [Test]
        public void Discard_action_should_discard_message()
        {
            var discardAction = new Discard("not needed anymore");
            var errorContext = new ErrorContext(new Exception(""), new Dictionary<string, string>(), "some-id", Array.Empty<byte>(), new TransportTransaction(), 1, "my-endpoint", new ContextBag());
            var actionContext = new TestableRecoverabilityContext { ErrorContext = errorContext };

            var routingContexts = discardAction.GetRoutingContexts(actionContext);

            CollectionAssert.IsEmpty(routingContexts);
            Assert.AreEqual(discardAction.ErrorHandleResult, ErrorHandleResult.Handled);
        }
    }
}