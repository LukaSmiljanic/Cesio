using Platform.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddWorkerServices();
builder.Build().Run();
