using Capsule.Build;
using JAG.Capsule.Tiled;

return CapsuleBuild.Configure(args)
    .AddImporter(new TiledImporter())
    .WithTileSize(16)
    .Run();
