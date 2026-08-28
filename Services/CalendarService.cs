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
            if (!Directory.Exists(_watchFolder)) return all;
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
                        // Tekil ve tekrarlayan eventleri 7 günlük pencereye genişlet
                        var from = new CalDateTime(DateTime.Now);
                        var to = new CalDateTime(DateTime.Now.AddDays(7));
                        var occurrences = e.GetOccurrences(from, to).Take(10).ToList();
                        if (occurrences.Count > 0)
                        {
                            foreach (var occ in occurrences)
                            {
                                var s = occ.Period.StartTime.AsSystemLocal;
                                var ee = occ.Period.EndTime.AsSystemLocal;
                                if (ee < DateTime.Now) continue;
                                all.Add(new CalendarEvent(e.Summary ?? "(Başlıksız)", s, ee, e.Location ?? ""));
                            }
                        }
                        else
                        {
                            var start = e.DtStart?.AsSystemLocal ?? DateTime.MinValue;
                            var end = e.DtEnd?.AsSystemLocal ?? start.AddHours(1);
                            if (end < DateTime.Now) continue;
                            if (start < DateTime.Now.AddDays(-1)) continue;
                            all.Add(new CalendarEvent(e.Summary ?? "(Başlıksız)", start, end, e.Location ?? ""));
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        // Tarihe göre sırala ve tekrarları ayıkla (aynı başlık+start çakışması)
        return all.OrderBy(x => x.Start).GroupBy(x => (x.Title, x.Start)).Select(g => g.First()).Take(max).ToList();
    }

    public record CalendarEvent(string Title, DateTime Start, DateTime End, string Location);
}
