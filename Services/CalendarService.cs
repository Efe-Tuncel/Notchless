using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Notchless.Services;

public sealed class CalendarService
{
    private readonly string _watchFolder;

    public CalendarService()
    {
        _watchFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchless", "Calendar");
        Directory.CreateDirectory(_watchFolder);
    }

    public string WatchFolder => _watchFolder;

    public IReadOnlyList<CalendarEvent> LoadUpcomingEvents(int max = 5)
    {
        var all = new List<CalendarEvent>();
        try
        {
            var icsFiles = Directory.GetFiles(_watchFolder, "*.ics");
            foreach (var f in icsFiles)
            {
                try
                {
                    var text = File.ReadAllText(f);
                    var cal = Calendar.Load(text);
                    if (cal == null) continue;
                    foreach (var e in cal.Events)
                    {
                        var start = e.DtStart?.AsSystemLocal ?? DateTime.MinValue;
                        var end = e.DtEnd?.AsSystemLocal ?? start.AddHours(1);
                        if (end < DateTime.Now) continue; // past
                        // expand recurring roughly for next 7 days
                        var occurrences = e.GetOccurrences(new CalDateTime(DateTime.Now), new CalDateTime(DateTime.Now.AddDays(7)));
                        foreach (var occ in occurrences.Take(10))
                        {
                            var s = occ.Period.StartTime.AsSystemLocal;
                            var ee = occ.Period.EndTime.AsSystemLocal;
                            if (ee < DateTime.Now) continue;
                            all.Add(new CalendarEvent(e.Summary ?? "(Başlıksız)", s, ee, e.Location ?? ""));
                        }
                        if (!occurrences.Any() && start >= DateTime.Now.AddDays(-1))
                            all.Add(new CalendarEvent(e.Summary ?? "(Başlıksız)", start, end, e.Location ?? ""));
                    }
                }
                catch { }
            }
        }
        catch { }
        return all.OrderBy(x => x.Start).Take(max).ToList();
    }

    public record CalendarEvent(string Title, DateTime Start, DateTime End, string Location);
}
