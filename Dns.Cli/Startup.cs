using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
using Dns.Cli.Extensions;
using Dns.Cli.Middleware;
using Dns.Config;
using Dns.Contracts;
using Dns.Handlers;
using Dns.Services;
using Dns.ZoneProvider.AP;
using Dns.ZoneProvider.Bind;
using Dns.ZoneProvider.IPProbe;
using Dns.ZoneProvider.Traefik;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Dns.Cli;

public class Startup(IConfiguration configuration)
{
    private readonly IConfigurationRoot _configuration = (IConfigurationRoot)configuration;

    public void ConfigureServices(IServiceCollection services)
	{
        services.AddAutoMapper(typeof(Startup).Assembly);

		services.AddOptions();
		services.Configure<JsonSerializerOptions>(opts =>
		{
			opts.Converters.Add(new JsonStringEnumConverter());
			opts.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
			opts.NumberHandling = JsonNumberHandling.AllowReadingFromString;
		});
		services.AddOptions<ServerOptions>()
		           .Configure<IConfiguration, IOptions<JsonSerializerOptions>>(
			           (opt, cfg, json) =>
			           {
				           var section = cfg.GetRequiredSection("server");

				           var element = section.ReadJsonElement();

				           var parsed = element.Deserialize<ServerOptions>(json.Value)
				                        ?? throw new InvalidOperationException("Failed to deserialize ServerOptions.");

				           opt.Zones       = parsed.Zones;
				           opt.DnsListener = parsed.DnsListener;
				           opt.WebServer   = parsed.WebServer;
			           });
		/*
		services.AddOptions<DatabaseSettings>().Bind(_configuration.GetSection(nameof(DatabaseSettings)));

		var databaseSettings = _configuration
		                  .GetSection(nameof(DatabaseSettings))
		                  .Get<DatabaseSettings>()
		                  ?? throw new InvalidOperationException("Missing DatabaseSettings configuration.");
		*/
		var appConfig = configuration.Get<AppConfig>();

		services.AddSingleton(configuration);
		services.AddSingleton(appConfig!);
		services.AddSingleton<IDnsServer, DnsServer>();
		services.AddTransient<TraefikClientHandler>();
		services.AddHttpClient<TraefikClientService>().ConfigurePrimaryHttpMessageHandler<TraefikClientHandler>();

		#region Providers

		services.AddTransient<IPProbeZoneProvider>();
		services.AddTransient<BindZoneProvider>();
		services.AddTransient<APZoneProvider>();
		services.AddTransient<TraefikZoneProvider>();

		#endregion

		#region Resolvers

		services.AddTransient<IDnsResolver, SmartZoneResolver>();

		#endregion

		services.AddCors();

		services.AddResponseCompression(
			options =>
			{
				options.EnableForHttps = true;
				options.Providers.Add<GzipCompressionProvider>();
			}
		);

		services.AddLogging(
			configure =>
			{
				configure.AddSimpleConsole(options =>
				{
					options.IncludeScopes   = true;
					options.SingleLine      = true;
					options.TimestampFormat = "[hh:mm:ss] ";
					options.ColorBehavior   = LoggerColorBehavior.Enabled;
				});
			}
		);

		services.AddControllers(
			        options =>
			        {
				        options.RespectBrowserAcceptHeader = true;
				        options.EnableEndpointRouting      = false;
				        //AuthorizationPolicy policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
				        //options.Filters.Add( new AuthorizeFilter( policy ) );
			        }
		        )
		        .AddMvcOptions(
			        o => o.OutputFormatters.Add(
				        new XmlSerializerOutputFormatter(new XmlWriterSettings { Indent = true })
			        )
		        )
		        .AddJsonOptions(
			        options => options.JsonSerializerOptions.DefaultIgnoreCondition =
				        JsonIgnoreCondition.WhenWritingNull
		        );

		services.AddSwaggerGen(
			c =>
			{
				//c.IncludeXmlComments( Path.Combine( AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml" ) );
				c.SwaggerDoc("v1", new() { Title = "DNS Api", Version = "v1" });
				// Include 'SecurityScheme' to use JWT Authentication
				var jwtSecurityScheme = new OpenApiSecurityScheme
				{
					BearerFormat = "JWT",
					Name         = "Authorization",
					In           = ParameterLocation.Header,
					Type         = SecuritySchemeType.ApiKey,
					Scheme       = JwtBearerDefaults.AuthenticationScheme,
					Description  = "Put **_ONLY_** your JWT Bearer token on textbox below!",
					Reference = new()
					{
						Id = JwtBearerDefaults.AuthenticationScheme, Type = ReferenceType.SecurityScheme,
					},
				};

				c.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);

				c.AddSecurityRequirement(new() { { jwtSecurityScheme, Array.Empty<string>() } });
			}
		);

		services.AddHttpContextAccessor();

		services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
		        .AddJwtBearer(
			        options =>
			        {
				        options.TokenValidationParameters = new()
				        {
					        ValidateIssuerSigningKey = true,
					        IssuerSigningKey =
						        new SymmetricSecurityKey(
							        Encoding.ASCII.GetBytes(
								        appConfig!.Server.WebServer.JwtSecretKey
							        )
						        ),
					        ValidateIssuer   = false,
					        ValidateAudience = false,
				        };
			        }
		        );

		services.AddSingleton<IServiceProvider, ServiceProvider>();


		#region MySQL Connection

		/*
		var connectionString = $"server={databaseSettings.Server};" +
		                          $"port={databaseSettings.Port};" +
		                          $"database={databaseSettings.Database};" +
		                          $"user={databaseSettings.Username};" +
		                          $"password={databaseSettings.Password}";
		   var mysqlServerVersion = ServerVersion.AutoDetect(connectionString);
		   services.AddDbContext<ListServerDbContext>(options => options.UseMySql(connectionString, mysqlServerVersion));

		services.AddScoped<IUserRepository, UserRepository>();
		*/

		#endregion

		services.AddSingleton<IDnsService, DnsService>();
		services.AddHostedService(p => p.GetRequiredService<IDnsService>());

		services.RemoveAll<IHttpMessageHandlerBuilderFilter>();
	}

	/// <summary>
	///
	/// </summary>
	/// <param name="app"></param>
	/// <param name="env"></param>
	public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
	{
		if (env.IsDevelopment())
			app.UseDeveloperExceptionPage();
		else
			app.UseHsts();

		app.UseMiddleware<PrependBearerSchemeMiddleware>();

		app.UseRouting();
		app.UseAuthentication();
		app.UseLoadCurrentUser();
		app.UseSwagger();
		app.UseSwaggerUI();

		app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
		app.UseHttpsRedirection();
		app.UseResponseCompression();

		app.UseAuthorization();
		app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
	}
}