using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.Contracts;

namespace UtaElectronicSignature.API.Controllers;

/// <summary>
/// Administracion de a que URL avisar por cada ClientId consumidor (HrBackend,
/// u otro sistema futuro). Ver ICallbackEndpointService/CallbackEndpointService.
/// </summary>
[ApiController, Route("api/v1/signature/admin/callback-endpoints"), Authorize(Policy = SignaturePermissions.ConfigManage)]
public sealed class CallbackEndpointsController(ICallbackEndpointService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CallbackEndpointResponse>>> List(CancellationToken ct)
        => Ok(await service.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CallbackEndpointResponse>> Create(CallbackEndpointCreateRequest request, CancellationToken ct)
        => Created("", await service.CreateAsync(request, ct));

    [HttpPut("{id:long}")]
    public async Task<ActionResult<CallbackEndpointResponse>> Update(long id, CallbackEndpointUpdateRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Deactivate(long id, CancellationToken ct)
    {
        await service.DeactivateAsync(id, ct);
        return NoContent();
    }
}
