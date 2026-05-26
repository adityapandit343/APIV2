using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatbotApi.Data;
using ChatbotApi.DTOs;
using ChatbotApi.Models;
using ChatbotApi.Services;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QnAController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IQuestionMatchingService _matcher;

    public QnAController(AppDbContext db, IQuestionMatchingService matcher)
    {
        _db = db;
        _matcher = matcher;
    }

    private int CurrentTenantId =>
        int.Parse(User.FindFirstValue("tenantId") ?? "0");

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var pairs = await _db.QnAPairs
            .Where(q => q.TenantId == CurrentTenantId)
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new QnAPairDto(q.Id, q.Question, q.Answer, q.IsActive, q.CreatedAt))
            .ToListAsync();
        return Ok(pairs);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var pair = await _db.QnAPairs
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == CurrentTenantId);
        if (pair == null) return NotFound();
        return Ok(new QnAPairDto(pair.Id, pair.Question, pair.Answer, pair.IsActive, pair.CreatedAt));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateQnAPairRequest req)
    {
        var pair = new QnAPair
        {
            TenantId = CurrentTenantId,
            Question = req.Question.Trim(),
            Answer = req.Answer.Trim()
        };
        _db.QnAPairs.Add(pair);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = pair.Id },
            new QnAPairDto(pair.Id, pair.Question, pair.Answer, pair.IsActive, pair.CreatedAt));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateQnAPairRequest req)
    {
        var pair = await _db.QnAPairs
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == CurrentTenantId);
        if (pair == null) return NotFound();

        pair.Question = req.Question.Trim();
        pair.Answer = req.Answer.Trim();
        pair.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new QnAPairDto(pair.Id, pair.Question, pair.Answer, pair.IsActive, pair.CreatedAt));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var pair = await _db.QnAPairs
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == CurrentTenantId);
        if (pair == null) return NotFound();
        _db.QnAPairs.Remove(pair);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Live preview endpoint — same matching logic used by the webhook
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(ChatPreviewRequest req)
    {
        var pairs = await _db.QnAPairs
            .Where(q => q.TenantId == CurrentTenantId && q.IsActive)
            .ToListAsync();

        var (match, _) = _matcher.FindBestMatch(req.Question, pairs);
        if (match != null)
            return Ok(new ChatPreviewResponse(match.Answer, true));

        return Ok(new ChatPreviewResponse(_matcher.GetFallbackMessage(), false));
    }
}
