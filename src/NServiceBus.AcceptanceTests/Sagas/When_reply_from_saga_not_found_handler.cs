﻿namespace NServiceBus.AcceptanceTests.Sagas
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using EndpointTemplates;
    using NServiceBus.Sagas;
    using NUnit.Framework;

    public class When_reply_from_saga_not_found_handler : NServiceBusAcceptanceTest
    {
        // related to NSB issue #2044
        [Test]
        public async Task It_should_invoke_message_handler()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>(b => b.When((session, c) => session.Send(new MessageToSaga())))
                .WithEndpoint<ReceiverWithSaga>()
                .Done(c => c.ReplyReceived)
                .Run();

            Assert.IsTrue(context.Logs.Any(m => m.Message.Equals("Could not find a started saga of 'NServiceBus.AcceptanceTests.Sagas.When_reply_from_saga_not_found_handler+ReceiverWithSaga+NotFoundHandlerSaga1' for message type 'NServiceBus.AcceptanceTests.Sagas.When_reply_from_saga_not_found_handler+MessageToSaga'.")));
            Assert.IsTrue(context.Logs.Any(m => m.Message.Equals("Could not find a started saga of 'NServiceBus.AcceptanceTests.Sagas.When_reply_from_saga_not_found_handler+ReceiverWithSaga+NotFoundHandlerSaga2' for message type 'NServiceBus.AcceptanceTests.Sagas.When_reply_from_saga_not_found_handler+MessageToSaga'.")));
            Assert.IsTrue(context.Logs.Count(m => m.Message.Equals("Could not find any started sagas for message type 'NServiceBus.AcceptanceTests.Sagas.When_reply_from_saga_not_found_handler+MessageToSaga'. Going to invoke SagaNotFoundHandlers.")) == 1);
            Assert.IsTrue(context.ReplyReceived);
        }

        public class Context : ScenarioContext
        {
            public bool ReplyReceived { get; set; }
        }

        public class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.ConfigureRouting().RouteToEndpoint(typeof(MessageToSaga), typeof(ReceiverWithSaga));
                });
            }

            public class ReplyHandler : IHandleMessages<Reply>
            {
                public ReplyHandler(Context context)
                {
                    testContext = context;
                }

                public Task Handle(Reply message, IMessageHandlerContext context)
                {
                    testContext.ReplyReceived = true;

                    return Task.CompletedTask;
                }

                Context testContext;
            }
        }

        public class ReceiverWithSaga : EndpointConfigurationBuilder
        {
            public ReceiverWithSaga()
            {
                EndpointSetup<DefaultServer>();
            }

            public class NotFoundHandlerSaga1 : Saga<NotFoundHandlerSaga1.NotFoundHandlerSaga1Data>, IAmStartedByMessages<StartSaga1>, IHandleMessages<MessageToSaga>
            {
                public Task Handle(StartSaga1 message, IMessageHandlerContext context)
                {
                    Data.ContextId = message.ContextId;
                    return Task.CompletedTask;
                }

                public Task Handle(MessageToSaga message, IMessageHandlerContext context)
                {
                    return Task.CompletedTask;
                }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<NotFoundHandlerSaga1Data> mapper)
                {
                    mapper.ConfigureMapping<StartSaga1>(m => m.ContextId)
                        .ToSaga(s => s.ContextId);
                    mapper.ConfigureMapping<MessageToSaga>(m => m.ContextId)
                        .ToSaga(s => s.ContextId);
                }

                public class NotFoundHandlerSaga1Data : ContainSagaData
                {
                    public virtual Guid ContextId { get; set; }
                }
            }

            public class NotFoundHandlerSaga2 : Saga<NotFoundHandlerSaga2.NotFoundHandlerSaga2Data>, IAmStartedByMessages<StartSaga2>, IHandleMessages<MessageToSaga>
            {
                public Task Handle(StartSaga2 message, IMessageHandlerContext context)
                {
                    Data.ContextId = message.ContextId;
                    return Task.CompletedTask;
                }

                public Task Handle(MessageToSaga message, IMessageHandlerContext context)
                {
                    return Task.CompletedTask;
                }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<NotFoundHandlerSaga2Data> mapper)
                {
                    mapper.ConfigureMapping<StartSaga2>(m => m.ContextId)
                        .ToSaga(s => s.ContextId);
                    mapper.ConfigureMapping<MessageToSaga>(m => m.ContextId)
                        .ToSaga(s => s.ContextId);
                }

                public class NotFoundHandlerSaga2Data : ContainSagaData
                {
                    public virtual Guid ContextId { get; set; }
                }
            }

            public class SagaNotFound : IHandleSagaNotFound
            {
                public Task Handle(object message, IMessageProcessingContext context)
                {
                    return context.Reply(new Reply());
                }
            }
        }

        public class StartSaga1 : ICommand
        {
            public Guid ContextId { get; set; }
        }

        public class StartSaga2 : ICommand
        {
            public Guid ContextId { get; set; }
        }

        public class MessageToSaga : ICommand
        {
            public Guid ContextId { get; set; }
        }

        public class Reply : IMessage
        {
        }
    }
}