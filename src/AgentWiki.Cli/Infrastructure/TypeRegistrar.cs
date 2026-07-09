using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Infrastructure;

/// <summary>
/// Bridges Microsoft.Extensions.DependencyInjection with Spectre.Console.Cli.
/// </summary>
public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    /// <inheritdoc />
    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());

    /// <inheritdoc />
    public void Register(Type service, Type implementation) =>
        services.AddSingleton(service, implementation);

    /// <inheritdoc />
    public void RegisterInstance(Type service, object implementation) =>
        services.AddSingleton(service, implementation);

    /// <inheritdoc />
    public void RegisterLazy(Type service, Func<object> factory) =>
        services.AddSingleton(service, _ => factory());
}

/// <summary>
/// Resolves command and service types from a root <see cref="IServiceProvider"/>.
/// </summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    /// <inheritdoc />
    public object? Resolve(Type? type) =>
        type is null ? null : provider.GetService(type);

    /// <inheritdoc />
    public void Dispose()
    {
        if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
