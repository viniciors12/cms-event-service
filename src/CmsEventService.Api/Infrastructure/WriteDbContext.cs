using Microsoft.EntityFrameworkCore;

namespace CmsEventService.Api.Infrastructure;

/// <summary>The writer: used by event ingestion and the admin override. Owns schema creation.</summary>
public class WriteDbContext(DbContextOptions<WriteDbContext> options) : CmsDbContextBase(options);
