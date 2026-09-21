# Important

No error message when trying to delete a parent snapshot.

Compact the project file on save:
* Redefine ZOrder into a single suborder
* Remove any deleted components
* Remove any unused prototypes, datasets, etc
Deleted records stick around in save files forever at the moment.

When a player leaves, their hand and cursor is orphaned.

Nodes are both state and view, like with the Y animation. Consider getting rid of Y animation.

Make the host source 1 and use source 0 for system events like persisted events.

Cursors slide behind cards on the upper half.

Can't mass delete objects.

# Later

I'm not going to fix these yet.

Add assertions to events, like checking that the suborder is actually ordered.

Allow players to Undo things that another player did, possible with Ctrl+Shift+Z

# Other issues

These are not planned for my current branch.

VisualCommands, keyboard shortcuts, and Event Actions should be combined.

VcDie doesn't cache its template like VcToken does.

Upload local images.

Orthographic camera with a heavy skew for depth in 2D.

Create an "on start" shuffle for snapshots that reshuffles using a new seed.

Allow for shuffling of arbitrary components, like in screentop.