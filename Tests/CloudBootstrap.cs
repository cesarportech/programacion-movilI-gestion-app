// Explicit local maintenance harness, linked only into checks, never shipped in MAUI.
// Uses the existing master identity to prepare a backup/upload without persisting a login token.
using System.Text.Json;
using GestionLibros.Models;
namespace GestionLibros.Data;
public sealed partial class AppDatabase
{
    public async Task MaintenanceExportCloud(string output)
    {
        await Initialize();
        var master = (await db.Table<LocalUser>().ToListAsync()).First(x => x.IsMaster && !x.Disabled);
        StartSession(master, DateTime.UtcNow, DateTime.UtcNow);
        var pending = await PrepareCloudUploads();
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Dataset = await CloudDataset(), UserId = master.Id, Version = master.SecurityVersion, Pending = pending }));
        Logout();
        Console.WriteLine($"Prepared {pending.Count} scoped uploads; credentials were not printed.");
    }
    public async Task MaintenanceAckCloud(string input)
    {
        await Initialize();
        var master = (await db.Table<LocalUser>().ToListAsync()).First(x => x.IsMaster && !x.Disabled);
        StartSession(master, DateTime.UtcNow, DateTime.UtcNow);
        foreach (var ack in JsonSerializer.Deserialize<List<Ack>>(await File.ReadAllTextAsync(input))!)
            await AcknowledgeCloudUpload(ack.Scope, ack.RequestId, ack.Revision);
        Logout(); Console.WriteLine("Cloud confirmations recorded locally.");
    }
    private record Ack(string Scope, string RequestId, long Revision);
}
