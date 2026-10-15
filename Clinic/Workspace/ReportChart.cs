using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Clinic.Workspace;
public sealed record ChartPoint(string Label, string Display, double Percent);
public sealed record ReportChart(string Title, IReadOnlyList<ChartPoint> Points)
{
    public bool HasPoints => Points.Count > 0;
    public static IEnumerable<ReportChart> Build(IEnumerable<Record> source)
    {
        var rows=source.ToArray();
        foreach(var (kind,title,field,unit) in new[] {("attendance","Посещаемость","total","записей"),("load","Загрузка врачей","load_percent","%"),
            ("morbidity","Приёмы по диагнозам","visits","случаев"),("revenue","Выручка","amount","₽")})
        {
            var selected=rows.Where(r=>r.Values.GetValueOrDefault("report_kind")==kind).ToArray();
            var points=selected.Select(r=>new {Label=r.Cell1.Contains(':')?r.Cell1[(r.Cell1.IndexOf(':')+1)..].Trim():r.Cell1,
                Value=decimal.TryParse(r.Values.GetValueOrDefault(field),NumberStyles.Number,CultureInfo.InvariantCulture,out var v)?v:0}).ToArray();
            var max=kind=="load"?100:points.Select(p=>p.Value).DefaultIfEmpty().Max();
            yield return new(title,points.Select(p=>new ChartPoint(p.Label,p.Value.ToString("0.##",CultureInfo.GetCultureInfo("ru-RU"))+" "+Unit(kind,p.Value,unit),max==0?0:(double)(p.Value/max*100))).ToArray());
        }
    }
    private static string Unit(string kind,decimal value,string original)
    {
        if(kind is not ("attendance" or "morbidity"))return original;
        var number=(int)value;var many=number%100 is >=11 and <=14;
        if(kind=="attendance")return many?"записей":number%10==1?"запись":number%10 is >=2 and <=4?"записи":"записей";
        return many?"случаев":number%10==1?"случай":number%10 is >=2 and <=4?"случая":"случаев";
    }

}
