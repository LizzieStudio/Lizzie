# Lizzie Studio

A single application for tabletop game designers to make it faster to
iterate, prototype, playtest, and demo. Free and open source. 

## Installation

Download Godot and open the project using the Godot application.

## Features and Roadmap

* 3D and 2D mode.
* No physics engine (other than moving and placing objects with collisions).
* Built in component creation tools.

## Formatting

To format the project, run the following command from the project root:

```bash
dotnet tool restore
dotnet csharpier format .
```

## Testing

To run the tests, use the command

```bash
dotnet test
```

## Build the docs

To build the docs, run

```bash
dotnet tool restore
rm -rf docs/api docs/_site && dotnet docfx docs/docfx.json
```

To see the docs on localhost, run

```bash
dotnet tool restore
rm -rf docs/api docs/_site && dotnet docfx docs/docfx.json --serve
```

### Pre-commit Hook

A pre-commit hook is provided to catch formatting issues before committing.
Install it once after cloning:

```bash
cp hooks/pre-commit .git/hooks/pre-commit
```

This is only a check. It's best to configure CSharpier to format on save in
whatever editor you use.

## License

[MIT](https://opensource.org/licenses/MIT).
