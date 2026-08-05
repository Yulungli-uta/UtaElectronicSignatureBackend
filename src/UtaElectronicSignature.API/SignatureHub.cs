using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace UtaElectronicSignature.API;
[Authorize]
public sealed class SignatureHub:Hub
{
    public Task JoinProcess(long processId)=>Groups.AddToGroupAsync(Context.ConnectionId,$"process:{processId}");
}
