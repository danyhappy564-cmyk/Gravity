using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Common.Models.Logging;

namespace Gravity.Core;

/// <summary>서버 데이터가 다 올라온 뒤 한 번: 설정·감지·맵 정보 준비, 패치 켜기.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class GravityLoader(ISptLogger<GravityLoader> logger, ModHelper modHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var dir = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        logger.Info($"{GravityInfo.Tag} {GravityInfo.Version} 로드 ({dir})");
        return Task.CompletedTask;
    }
}
