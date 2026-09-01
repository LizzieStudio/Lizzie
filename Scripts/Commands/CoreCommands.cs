using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lizzie.Scripts.Commands
{
    [Command(VisualCommand.Tuck)]
    public class TuckCommand : CommandBase
    {
        public TuckCommand()
        {
            Caption = "Tuck";
            Command = VisualCommand.Tuck;
        }
    }

    [Command(VisualCommand.Untuck)]
    public class UntuckCommand : CommandBase
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
    public class UnfreezeCommand : CommandBase
    {
        public UnfreezeCommand()
        {
            Caption = "Unfreeze";
            Command = VisualCommand.Unfreeze;
        }
    }

    [Command(VisualCommand.Refresh)]
    public class RefreshCommand : CommandBase
    {
        public RefreshCommand()
        {
            Caption = "Refresh";
            Command = VisualCommand.Refresh;
        }
    }

    [Command(VisualCommand.Duplicate)]
    public class DuplicateCommand : CommandBase
    {
        public DuplicateCommand()
        {
            Caption = "Duplicate";
            Command = VisualCommand.Duplicate;
        }
    }

    [Command(VisualCommand.Edit)]
    public class EditCommand : CommandBase
    {
        public EditCommand()
        {
            Caption = "Edit";
            Command = VisualCommand.Edit;
            SingleOnly = true;
        }
    }

    [Command(VisualCommand.MakeUnique)]
    public class MakeUniqueCommand : CommandBase
    {
        public MakeUniqueCommand()
        {
            Caption = "Make Unique";
            Command = VisualCommand.MakeUnique;
            SingleOnly = true;
        }
    }

    [Command(VisualCommand.Delete)]
    public class DeleteCommand : CommandBase
    {
        public DeleteCommand()
        {
            Caption = "Delete";
            Command = VisualCommand.Delete;
            SingleOnly = true;
        }
    }

    [Command(VisualCommand.Draw)]
    public class DrawCommand : CommandBase
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
    public class DealCommand : CommandBase
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
