using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Threading.Tasks;

namespace Lizzie.Scripts.Commands
{
    [Command(VisualCommand.RotateCw)]
    public class RotateComponentCwCommand : CommandBase
    {
        public RotateComponentCwCommand()
        {
            Caption = "Rotate CW";
            Command = VisualCommand.RotateCw;
        }
    }

    [Command(VisualCommand.RotateCcw)]
    public class RotateComponentCcwCommand : CommandBase
    {
        public RotateComponentCcwCommand()
        {
            Caption = "Rotate CCW";
            Command = VisualCommand.RotateCcw;
        }
    }
}
