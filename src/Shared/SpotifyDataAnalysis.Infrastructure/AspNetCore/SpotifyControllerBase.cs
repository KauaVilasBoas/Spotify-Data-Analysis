using Microsoft.AspNetCore.Mvc;

namespace SpotifyDataAnalysis.Infrastructure.AspNetCore;

/// <summary>
/// Base class for every SpotifyDataAnalysis MVC controller.
///
/// Lives in shared Infrastructure (not the Host) on purpose: module controllers are hosted inside each
/// module's Application project, co-located with their CQRS handlers. The Host cannot be referenced by a
/// module, so a Host-based base class would be unreachable. Shared Infrastructure is referenced by every
/// module and already carries Microsoft.AspNetCore.App, making it the single reachable home for the base
/// type.
///
/// Controllers dispatch via <c>IMediator</c> and NEVER touch repositories, DbContext or Dapper directly.
/// </summary>
[ApiController]
public abstract class SpotifyControllerBase : ControllerBase
{
}
