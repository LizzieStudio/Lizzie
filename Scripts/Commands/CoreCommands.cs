using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lizzie.Scripts.Commands
{
    [Command(VisualCommand.Tuck)]
    public class TuckCommand : BasicCommand
    {
        public TuckCommand()
        {
            Caption = "Tuck";
            Command = VisualCommand.Tuck;
        }
    }

    [Command(VisualCommand.Untuck)]
    public class UntuckCommand : BasicCommand
    {
        public UntuckCommand()
        {
            Caption = "Untuck";
            Command = VisualCommand.Untuck;
        }
    }

    [Command(VisualCommand.Freeze)]
    public class FreezeCommand : CommandBase
    {
        public FreezeCommand()
        {
            Caption = "Freeze";
            Command = VisualCommand.Freeze;
        }
    }

    [Command(VisualCommand.Unfreeze)]
    public class UnfreezeCommand : BasicCommand
    {
        public UnfreezeCommand()
        {
            Caption = "Unfreeze";
            Command = VisualCommand.Unfreeze;
        }
    }

    [Command(VisualCommand.Refresh)]
    public class RefreshCommand : BasicCommand
    {
        public RefreshCommand()
        {
            Caption = "Refresh";
            Command = VisualCommand.Refresh;
        }
    }

    [Command(VisualCommand.Duplicate)]
    public class DuplicateCommand : BasicCommand
    {
        public DuplicateCommand()
        {
            Caption = "Duplicate";
            Command = VisualCommand.Duplicate;
        }
    }

    [Command(VisualCommand.Edit)]
    public class EditCommand : BasicCommand
    {
        public EditCommand()
        {
            Caption = "Edit";
            Command = VisualCommand.Edit;
            SingleOnly = true;
        }
    }

    [Command(VisualCommand.MakeUnique)]
    public class MakeUniqueCommand : BasicCommand
    {
        public MakeUniqueCommand()
        {
            Caption = "Make Unique";
            Command = VisualCommand.MakeUnique;
            SingleOnly = true;
        }
    }

    [Command(VisualCommand.Delete)]
    public class DeleteCommand : BasicCommand
    {
        public DeleteCommand()
        {
            Caption = "Delete";
            Command = VisualCommand.Delete;
            SingleOnly = true;
        }
    }

    [Command(VisualCommand.Draw)]
    public class DrawCommand : BasicCommand
    {
        public DrawCommand()
        {
            Caption = "Draw";
            Command = VisualCommand.Draw;
            SingleOnly = true;
            AddQtySubmenu = true;
        }
    }

    [Command(VisualCommand.Deal)]
    public class DealCommand : BasicCommand
    {
        public DealCommand()
        {
            Caption = "Deal";
            Command = VisualCommand.Deal;
            SingleOnly = true;
            AddQtySubmenu = true;
        }
    }
}
