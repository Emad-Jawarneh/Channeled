using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Infrastructure.Storage;
using Channeled.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChanneledInfrastructure(
        this IServiceCollection services,
        Action<FileStoreOptions>? configureFileStore = null)
    {
        if (configureFileStore is not null)
        {
            services.Configure(configureFileStore);
        }

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFileStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FileStoreOptions>>().Value;
            return options.Kind switch
            {
                FileStoreKind.FileSystem => new FilesystemFileStore(options),
                FileStoreKind.S3Compatible => new S3FileStore(options),
                _ => throw new InvalidOperationException($"Unknown file store kind '{options.Kind}'."),
            };
        });

        return services;
    }
}
