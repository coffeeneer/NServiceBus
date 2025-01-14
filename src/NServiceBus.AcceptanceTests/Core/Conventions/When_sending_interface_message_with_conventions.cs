﻿namespace NServiceBus.AcceptanceTests.Core.Conventions
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using EndpointTemplates;
    using NUnit.Framework;

    public class When_sending_interface_message_with_conventions : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_receive_the_message()
        {
            var context = await Scenario.Define<Context>(c => { c.Id = Guid.NewGuid(); })
                .WithEndpoint<Sender>(b => b.When(async (session, c) =>
                {
                    await session.Send<IMyInterfaceMessage>(m => m.Id = c.Id);
                }))
                .WithEndpoint<Receiver>()
                .Done(c => c.MessageInterfaceReceived)
                .Run();

            Assert.True(context.MessageInterfaceReceived);
        }

        public class Context : ScenarioContext
        {
            public bool MessageInterfaceReceived { get; set; }
            public Guid Id { get; set; }
        }

        public class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>(b =>
                {
                    b.Conventions().DefiningMessagesAs(type => type.Name.EndsWith("Message"));
                    b.ConfigureRouting().RouteToEndpoint(typeof(IMyInterfaceMessage), typeof(Receiver));
                }).ExcludeType<IMyInterfaceMessage>(); // remove that type from assembly scanning to simulate what would happen with true unobtrusive mode
            }
        }

        public class Receiver : EndpointConfigurationBuilder
        {
            public Receiver()
            {
                EndpointSetup<DefaultServer>(builder =>
                {
                    builder.Conventions()
                        .DefiningMessagesAs(type => type.Name.EndsWith("Message"));
                });
            }

            public class MyMessageInterfaceHandler : IHandleMessages<IMyInterfaceMessage>
            {
                public MyMessageInterfaceHandler(Context context)
                {
                    testContext = context;
                }

                public Task Handle(IMyInterfaceMessage interfaceMessage, IMessageHandlerContext context)
                {
                    if (testContext.Id != interfaceMessage.Id)
                    {
                        return Task.CompletedTask;
                    }

                    testContext.MessageInterfaceReceived = true;

                    return Task.CompletedTask;
                }

                Context testContext;
            }
        }

        public interface IMyInterfaceMessage
        {
            Guid Id { get; set; }
        }
    }
}