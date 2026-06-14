using Quartz;

public class PowergridSaveCommandHandler : ConsoleCommandHandler
{
    public PowergridSaveCommandHandler()
    {
        CommandName = "powergrid-save";
    }

    public override void Execute(string[] args)
    {
        if (PowergridUI.Current == null)
        {
            Console.PrintLine("powergrid-save: no powergrid window is open");
            return;
        }

        string name = args.Length > 0 ? args[0] : null;
        PowergridUI.Current.Save(name);
    }
}
