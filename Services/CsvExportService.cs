using System.Threading.Tasks;
using OcdWizard.ControlStation.Database;

namespace OcdWizard.ControlStation.Services;

public class CsvExportService(DatabaseService db)
{
    public Task ExportAsync(string sessionId, string outputPath) =>
        db.ExportToCsvAsync(sessionId, outputPath);
}
