using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Command(VisualCommand.MoveToTop)]
public  class MoveToTop : CommandBase
{
    public override Update Execute(IEnumerable<VisualComponentBase> components, GameObjects context)
    {
        int maxZ = 0;
        Dictionary<Guid, int> oldZOrder = new Dictionary<Guid, int>();
        List<VisualComponentBase> childrenList = new();
        
        foreach (var o in context.GetChildren())
        {
            if (o is not VisualComponentBase vcb) continue;
            
            maxZ = Mathf.Max(maxZ, vcb.ZOrder);
            oldZOrder.Add(vcb.Reference, vcb.ZOrder);
            childrenList.Add(vcb);
        }

        maxZ++;
        foreach (var c in components.OrderBy(x => x.ZOrder))
        {
            c.ZOrder = maxZ;
            maxZ++;
        }

        var u = new Update();
        
        var curZ = 0;
        foreach (var v in childrenList.OrderBy(x => x.ZOrder))
        {
            curZ++;
            var oldZ = oldZOrder[v.Reference];

            if (oldZ != curZ)
            {
                v.ZOrder = curZ;
                u.Add (new Change
                {
                    Action = Change.ChangeType.ZOrder,
                    Begin = oldZ,
                    End = v.ZOrder,
                    Component = v,
                });
            } 
            v.ZOrder = curZ;
            curZ++;
        }


        return u;
    }
}
