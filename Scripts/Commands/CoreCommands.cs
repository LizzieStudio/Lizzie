using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lizzie.Scripts.Commands
{
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
