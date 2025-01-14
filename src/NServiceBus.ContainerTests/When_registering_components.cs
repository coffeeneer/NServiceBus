#pragma warning disable CS0618
namespace NServiceBus.ContainerTests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Extensions.DependencyInjection;
    using NServiceBus;
    using NUnit.Framework;


    public class When_registering_components
    {
        [Test]
        public void Multiple_registrations_of_the_same_component_should_be_allowed()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(typeof(DuplicateClass), DependencyLifecycle.InstancePerCall);
            serviceCollection.ConfigureComponent(typeof(DuplicateClass), DependencyLifecycle.InstancePerCall);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.AreEqual(2, serviceProvider.GetServices(typeof(DuplicateClass)).Count());
        }

        [Test]
        public void Should_support_lambdas_that_uses_other_components_registered_later()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(s => ((StaticFactory)s.GetService(typeof(StaticFactory))).Create(), DependencyLifecycle.InstancePerCall);
            serviceCollection.ConfigureComponent(() => new StaticFactory(), DependencyLifecycle.SingleInstance);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.NotNull(serviceProvider.GetService(typeof(ComponentCreatedByFactory)));
        }

        [Test]
        public void A_registration_should_be_allowed_to_be_updated()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(typeof(ISingletonComponent), new SingletonComponent());
            serviceCollection.AddSingleton(typeof(ISingletonComponent), new AnotherSingletonComponent());

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.IsInstanceOf<AnotherSingletonComponent>(serviceProvider.GetService(typeof(ISingletonComponent)));
        }

        [Test]
        public void Register_singleton_should_be_supported()
        {
            var singleton = new SingletonComponent();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(typeof(ISingletonComponent), singleton);
            serviceCollection.AddSingleton(typeof(SingletonComponent), singleton);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.AreEqual(serviceProvider.GetService(typeof(SingletonComponent)), singleton);
            Assert.AreEqual(serviceProvider.GetService(typeof(ISingletonComponent)), singleton);
        }

        [Test]
        public void Registering_the_same_singleton_for_different_interfaces_should_be_supported()
        {
            var serviceCollection = new ServiceCollection();
            var singleton = new SingletonThatImplementsToInterfaces();
            serviceCollection.AddSingleton(typeof(ISingleton1), singleton);
            serviceCollection.AddSingleton(typeof(ISingleton2), singleton);
            serviceCollection.ConfigureComponent(typeof(ComponentThatDependsOnMultiSingletons), DependencyLifecycle.InstancePerCall);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var dependency = (ComponentThatDependsOnMultiSingletons)serviceProvider.GetService(typeof(ComponentThatDependsOnMultiSingletons));

            Assert.NotNull(dependency.Singleton1);
            Assert.NotNull(dependency.Singleton2);

            Assert.AreEqual(serviceProvider.GetService(typeof(ISingleton1)), singleton);
            Assert.AreEqual(serviceProvider.GetService(typeof(ISingleton2)), singleton);
        }

        [Test]
        public void Concrete_classes_should_get_the_same_lifecycle_as_their_interfaces()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(typeof(SingletonComponent), DependencyLifecycle.SingleInstance);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.AreSame(serviceProvider.GetService(typeof(SingletonComponent)), serviceProvider.GetService(typeof(ISingletonComponent)));
        }

        [Test]
        public void All_implemented_interfaces_should_be_registered()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(typeof(ComponentWithMultipleInterfaces),
                DependencyLifecycle.InstancePerCall);
            Assert.True(serviceCollection.HasComponent(typeof(ISomeInterface)));
            Assert.True(serviceCollection.HasComponent(typeof(ISomeOtherInterface)));
            Assert.True(serviceCollection.HasComponent(typeof(IYetAnotherInterface)));

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.AreEqual(1, serviceProvider.GetServices(typeof(IYetAnotherInterface)).Count());
        }

        [Test]
        public void All_implemented_interfaces_should_be_registered_for_func()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(() => new ComponentWithMultipleInterfaces(), DependencyLifecycle.InstancePerCall);
            Assert.True(serviceCollection.HasComponent(typeof(ISomeInterface)));
            Assert.True(serviceCollection.HasComponent(typeof(ISomeOtherInterface)));
            Assert.True(serviceCollection.HasComponent(typeof(IYetAnotherInterface)));

            var serviceProvider = serviceCollection.BuildServiceProvider();

            Assert.AreEqual(1, serviceProvider.GetServices(typeof(IYetAnotherInterface)).Count());
        }

        [Test]
        public void Multiple_implementations_should_be_supported()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(typeof(SomeClass), DependencyLifecycle.InstancePerUnitOfWork);
            serviceCollection.ConfigureComponent(typeof(SomeOtherClass), DependencyLifecycle.InstancePerUnitOfWork);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.NotNull(serviceProvider.GetService(typeof(SomeClass)));
            Assert.AreEqual(2, serviceProvider.GetServices(typeof(ISomeInterface)).Count());

            using (var scope = serviceProvider.CreateScope())
            {
                Assert.NotNull(scope.ServiceProvider.GetService(typeof(SomeClass)));
                Assert.AreEqual(2, scope.ServiceProvider.GetServices(typeof(ISomeInterface)).Count());
            }
        }

        [Test]
        public void Given_lookupType_should_be_used_as_service_in_the_registration_when_RegisterSingleton()
        {
            var serviceCollection = new ServiceCollection();
            var expected = new InheritedFromSomeClass();
            serviceCollection.AddSingleton(typeof(SomeClass), expected);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            Assert.AreEqual(expected, serviceProvider.GetService(typeof(SomeClass)));

            using (var scope = serviceProvider.CreateScope())
            {
                Assert.AreEqual(expected, scope.ServiceProvider.GetService(typeof(SomeClass)));
            }
        }

        [Test]
        public void Generic_interfaces_should_be_registered()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(typeof(ComponentWithGenericInterface),
                DependencyLifecycle.InstancePerCall);

            Assert.True(serviceCollection.HasComponent(typeof(ISomeGenericInterface<string>)));
        }

        [Test]
        [Ignore("Not sure that we should enforce this")]
        public void System_interfaces_should_not_be_auto_registered()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureComponent(typeof(ComponentWithSystemInterface),
                DependencyLifecycle.InstancePerCall);

            Assert.False(serviceCollection.HasComponent(typeof(IGrouping<string, string>)));
            Assert.False(serviceCollection.HasComponent(typeof(IDisposable)));
        }
    }


    public class ComponentThatDependsOnMultiSingletons
    {
        public ComponentThatDependsOnMultiSingletons(ISingleton1 singleton1, ISingleton2 singleton2)
        {
            Singleton1 = singleton1;
            Singleton2 = singleton2;
        }

        public ISingleton1 Singleton1 { get; private set; }
        public ISingleton2 Singleton2 { get; private set; }
    }

    public class SingletonThatImplementsToInterfaces : ISingleton2
    {
    }

    public interface ISingleton2 : ISingleton1
    {
    }

    public interface ISingleton1
    {
    }

    public class ComponentWithMultipleInterfaces : ISomeInterface, ISomeOtherInterface
    {
    }

    public class ComponentWithGenericInterface : ISomeGenericInterface<string>
    {
    }

    public class ComponentWithSystemInterface : IGrouping<string, string>, IDisposable
    {
        public IEnumerator<string> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public string Key => throw new NotImplementedException();

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }

    public interface ISomeGenericInterface<T>
    {
    }

    public interface ISomeOtherInterface : IYetAnotherInterface
    {
    }

    public interface IYetAnotherInterface
    {
    }

    public class DuplicateClass
    {
        public bool SomeProperty { get; set; }
        public bool AnotherProperty { get; set; }
    }

    public interface IWithSetterDependencies
    {
    }

    public class SomeClass : ISomeInterface
    {
    }

    public class InheritedFromSomeClass : SomeClass
    {
    }

    public class SomeOtherClass : ISomeInterface
    {
    }

    public interface ISomeInterface
    {
    }

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public enum SomeEnum
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        X
    }
}
#pragma warning restore CS0618