# Important

Dataset rows should be identified by a SnowTag. This would allow edits, insertion, and deletion to work correctly. For example, if I delete an unused row, cards still point at the correct row, because the SnowTag for each row hasn't changed.

Make prototypes, datasets, templates, and assets immutable records.

No error message when trying to delete a parent snapshot.

Compact the project file on save:
* Redefine ZOrder into a single suborder
* Remove any deleted components
* Remove any unused prototypes, datasets, etc

When a player leaves, their hand and cursor is orphaned.

Nodes are both state and view, like with the Y animation. Consider getting rid of Y animation.

# Later

I'm not going to fix these yet.

Add assertions to events, like checking that the suborder is actually ordered.

Players should pick their own seat color. There can be a starting default.

Allow players to Undo things that another player did, possible with Ctrl+Shift+Z

Deleted records stick around in save files forever at the moment.

# Other issues

These are not planned for my current branch.

Tokens need to fully handle their front and backs differently.
Like, you may want to use a dataset for the front, and an image for the back.

Can't mass delete objects.

VisualCommands, keyboard shortcuts, and Event Actions should be combined.

VcDie doesn't cache its template like VcToken does.

Upload local images.

Orthographic camera with a heavy skew for depth in 2D.

Create an "on start" shuffle for snapshots that reshuffles using a new seed.

Allow for shuffling of arbitrary components, like in screentop.