using DotCraft.Memory;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class MemoryRequestHandler(MemoryManagementService? memory) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.MemoryListStores, HandleListStoresAsync);
    }

    private Task<object?> HandleListStoresAsync(AppServerTypedRequest<Protocol.RpcEmpty> request, CancellationToken ct)
    {
        var service = memory ?? throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.MemoryListStores);
        return Task.FromResult<object?>(new Contract.MemoryListStoresResult
        {
            Stores = service.ListStores()
                .Select(static store => new Contract.MemoryStoreInfo
                {
                    Kind = store.Kind,
                    Scope = store.Scope,
                    Id = store.Id,
                    Path = store.Path,
                    DisplayName = store.DisplayName,
                    Writable = store.Writable
                })
                .ToArray()
        });
    }
}
