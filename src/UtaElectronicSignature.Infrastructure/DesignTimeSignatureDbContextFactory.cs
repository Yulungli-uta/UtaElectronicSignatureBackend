using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace UtaElectronicSignature.Infrastructure;
public sealed class DesignTimeSignatureDbContextFactory:IDesignTimeDbContextFactory<SignatureDbContext>
{
 public SignatureDbContext CreateDbContext(string[] args)
 {
  var options=new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=UtaElectronicSignatureDesign;Trusted_Connection=True").Options;
  return new SignatureDbContext(options);
 }
}
