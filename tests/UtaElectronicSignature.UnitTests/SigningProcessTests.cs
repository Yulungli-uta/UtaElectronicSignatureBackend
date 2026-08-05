using UtaElectronicSignature.Domain;
namespace UtaElectronicSignature.UnitTests;
public sealed class SigningProcessTests
{
 [Fact] public void Progress_counts_only_required_signed_participants()
 {
  var process=new SigningProcess{MinimumRequiredSignatures=2,Participants=[
   new(){Required=true,Status=ParticipantStatus.Signed},new(){Required=true,Status=ParticipantStatus.Pending},new(){Required=false,Status=ParticipantStatus.Signed}]};
  Assert.Equal(50m,process.Progress);
 }
 [Fact] public void New_process_has_stable_guid()=>Assert.NotEqual(Guid.Empty,new SigningProcess().ProcessGuid);
 [Fact] public void Progress_ignores_optional_unsigned_participants()
 {
  var process=new SigningProcess{MinimumRequiredSignatures=1,Participants=[
   new(){Required=true,Status=ParticipantStatus.Signed},new(){Required=false,Status=ParticipantStatus.Pending}]};
  Assert.Equal(100m,process.Progress);
 }
 [Theory]
 [InlineData(WorkflowType.Unordered)]
 [InlineData(WorkflowType.Sequential)]
 public void Supported_workflows_can_be_assigned(WorkflowType workflow)
 {
  var process=new SigningProcess{WorkflowType=workflow};
  Assert.Equal(workflow,process.WorkflowType);
 }
 [Fact] public void Document_versions_preserve_hash_chain_values()
 {
  var previous=System.Security.Cryptography.SHA256.HashData([1,2,3]);
  var current=System.Security.Cryptography.SHA256.HashData([4,5,6]);
  var version=new DocumentVersion{SequenceNumber=2,PreviousVersionID=1,PreviousSha256=previous,Sha256=current};
  Assert.Equal(2,version.SequenceNumber);
  Assert.Equal(previous,version.PreviousSha256);
  Assert.Equal(current,version.Sha256);
 }
}
