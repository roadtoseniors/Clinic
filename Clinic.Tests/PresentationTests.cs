using Clinic.Demo;
using Clinic.ViewModels;
using Clinic.Workspace;

internal static class PresentationTests
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
            checks++;
        }

        foreach (var login in new[]
        {
            "kirillov",
            "petrova",
            "sokolov",
            "admin"
        }

        )
        {
            var model = new MainViewModel(() => new DemoGateway())
            {
                Login = login,
                Password = "demo123"
            };
            await model.SignInCommand.ExecuteAsync(null);
            var workspace = model.Workspace!;
            var navigation = workspace.MainNavigation.Concat(workspace.ExtraNavigation).Concat(workspace.ClinicalNavigation).ToList();
            Check(workspace.MainNavigation.Count <= 5, "At most five main sections: " + login);
            Check(navigation.Select(i => i.Key).Distinct().Count() == workspace.Sections.Count, "All permitted sections remain accessible: " + login);
            Check(navigation.Count(i => i.IsActive) == 1, "Exactly one current section: " + login);
            Check(!workspace.AreFiltersOpen && !workspace.IsExtraNavigationOpen && !workspace.IsClinicalNavigationOpen, "Secondary controls start collapsed: " + login);
            var target = workspace.ExtraNavigation.FirstOrDefault() ?? workspace.MainNavigation.Last();
            workspace.NavigateCommand.Execute(target.Key);
            await workspace.RefreshCommand.ExecuteAsync(null);
            Check(target.IsActive && navigation.Count(i => i.IsActive) == 1, "Navigation updates the selected item: " + login);
            if (workspace.ExtraNavigation.Contains(target))
                Check(workspace.IsExtraNavigationOpen, "Selected additional section stays visible: " + login);
            if (login is "petrova" or "sokolov")
            {
                workspace.NavigateCommand.Execute("appointments");
                await workspace.RefreshCommand.ExecuteAsync(null);
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                Check(workspace.FromDate == today && workspace.ToDate == today, "Staff starts with today's appointments");
                Check(workspace.Rows.All(r => r.Values["work_date"] == today), "No historical appointments in today's view");
                await workspace.SetDateRangeCommand.ExecuteAsync("all");
                Check(workspace.DateScopeLabel == "Все даты" && workspace.FromDate.Length == 0, "All dates can be selected");
            }

            workspace.ToggleFiltersCommand.Execute(null);
            Check(workspace.AreFiltersOpen, "Filters remain available: " + login);
            await TestUi.SignOutAsync(model);
        }

        Record Slot(string id, string doctor, string day, string time) => new()
        {
            Values = new()
            {
                ["slot"] = id,
                ["employee_id"] = doctor,
                ["work_date"] = day,
                ["start_time"] = time
            }
        };
        var slots = new[]
        {
            Slot("1|10:00", "1", "2026-10-15", "10:00"),
            Slot("1|09:00", "1", "2026-10-15", "09:00"),
            Slot("2|09:00", "2", "2026-10-15", "09:00"),
            Slot("3|09:00", "1", "2026-10-16", "09:00")
        };
        Check(WorkspaceViewModel.BookingTimes(slots, "1", "2026-10-15").Select(c => c.Name).SequenceEqual(new[] { "09:00", "10:00" }), "Booking shows only the selected doctor's times, sorted");
        Check(WorkspaceViewModel.BookingTimes(slots, "2", "2026-10-15").Count() == 1, "Switching doctor changes available times");
        Check(WorkspaceViewModel.BookingTimes(slots, "1", "2026-10-16").Single().Id == "3|09:00", "Switching day changes available times");
        Check(!WorkspaceViewModel.BookingTimes(slots, "1", "2026-10-17").Any(), "Empty day does not retain another day's slot");
        return checks;
    }
}
