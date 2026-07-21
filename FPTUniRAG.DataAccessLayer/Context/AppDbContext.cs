using System;
using System.Collections.Generic;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Context;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BenchmarkResult> BenchmarkResults { get; set; }

    public virtual DbSet<BenchmarkRun> BenchmarkRuns { get; set; }

    public virtual DbSet<Chapter> Chapters { get; set; }

    public virtual DbSet<ChatBenchmarkResult> ChatBenchmarkResults { get; set; }

    public virtual DbSet<ChatBenchmarkRun> ChatBenchmarkRuns { get; set; }

    public virtual DbSet<ChatModel> ChatModels { get; set; }

    public virtual DbSet<Chunk> Chunks { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentEmbeddingRun> DocumentEmbeddingRuns { get; set; }

    public virtual DbSet<EmbeddingSetting> EmbeddingSettings { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<StripeCheckoutTransaction> StripeCheckoutTransactions { get; set; }

    public virtual DbSet<ProcessingJob> ProcessingJobs { get; set; }

    public virtual DbSet<Session> Sessions { get; set; }

    public virtual DbSet<StudentActiveChatEntitlement> StudentActiveChatEntitlements { get; set; }

    public virtual DbSet<StudentFreeQuotaSetting> StudentFreeQuotaSettings { get; set; }

    public virtual DbSet<StudentSubscription> StudentSubscriptions { get; set; }

    public virtual DbSet<StudentTokenUsageCurrentDay> StudentTokenUsageCurrentDays { get; set; }

    public virtual DbSet<StudentTokenUsageCurrentMonth> StudentTokenUsageCurrentMonths { get; set; }

    public virtual DbSet<StudentTokenUsageCurrentWeek> StudentTokenUsageCurrentWeeks { get; set; }

    public virtual DbSet<Subject> Subjects { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<Teacher> Teachers { get; set; }

    public virtual DbSet<TeacherSubject> TeacherSubjects { get; set; }

    public virtual DbSet<TestQuestion> TestQuestions { get; set; }

    public virtual DbSet<TokenUsageLog> TokenUsageLogs { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<ChatModel>(entity =>
        {
            entity.HasKey(e => e.ChatModelId).HasName("chat_models_pkey");
            entity.ToTable("chat_models");
            entity.HasIndex(e => e.ModelName, "ux_chat_models_model_name").IsUnique();
            entity.Property(e => e.ChatModelId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("chat_model_id");
            entity.Property(e => e.ModelName)
                .HasMaxLength(150)
                .HasColumnName("model_name");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(200)
                .HasColumnName("display_name");
            entity.Property(e => e.ContextLength).HasColumnName("context_length");
            entity.Property(e => e.IsSelected).HasColumnName("is_selected");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<ChatBenchmarkRun>(entity =>
        {
            entity.HasKey(e => e.ChatBenchmarkRunId).HasName("chat_benchmark_runs_pkey");
            entity.ToTable("chat_benchmark_runs");
            entity.HasIndex(e => e.ModelName, "idx_chat_benchmark_runs_model");
            entity.HasIndex(e => e.BatchId, "idx_chat_benchmark_runs_batch");
            entity.Property(e => e.ChatBenchmarkRunId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("chat_benchmark_run_id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.ModelName)
                .HasMaxLength(150)
                .HasColumnName("model_name");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.PromptCount).HasColumnName("prompt_count");
            entity.Property(e => e.CompletedCount).HasColumnName("completed_count");
            entity.Property(e => e.SuccessCount).HasColumnName("success_count");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ExecutedBy).HasColumnName("executed_by");

            entity.HasOne(e => e.Subject).WithMany()
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_chat_benchmark_runs_subject");
        });

        modelBuilder.Entity<ChatBenchmarkResult>(entity =>
        {
            entity.HasKey(e => e.ResultId).HasName("chat_benchmark_results_pkey");
            entity.ToTable("chat_benchmark_results");
            entity.HasIndex(e => e.ChatBenchmarkRunId, "idx_chat_benchmark_results_run");
            entity.Property(e => e.ResultId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("result_id");
            entity.Property(e => e.ChatBenchmarkRunId).HasColumnName("chat_benchmark_run_id");
            entity.Property(e => e.PromptText).HasColumnName("prompt_text");
            entity.Property(e => e.AnswerText).HasColumnName("answer_text");
            entity.Property(e => e.RetrievedChunkCount).HasColumnName("retrieved_chunk_count");
            entity.Property(e => e.PromptTokens).HasColumnName("prompt_tokens");
            entity.Property(e => e.CompletionTokens).HasColumnName("completion_tokens");
            entity.Property(e => e.TotalTokens).HasColumnName("total_tokens");
            entity.Property(e => e.ResponseTimeMs).HasColumnName("response_time_ms");
            entity.Property(e => e.IsSuccess).HasColumnName("is_success");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(e => e.Run).WithMany(e => e.Results)
                .HasForeignKey(e => e.ChatBenchmarkRunId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_chat_benchmark_results_run");
        });

        modelBuilder.Entity<BenchmarkResult>(entity =>
        {
            entity.HasKey(e => e.ResultId).HasName("benchmark_results_pkey");

            entity.ToTable("benchmark_results");

            entity.HasIndex(e => e.BenchmarkRunId, "ix_benchmark_results_benchmark_run_id");

            entity.HasIndex(e => e.QuestionId, "ix_benchmark_results_question_id");

            entity.Property(e => e.ResultId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("result_id");
            entity.Property(e => e.BenchmarkRunId).HasColumnName("benchmark_run_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.QuestionId).HasColumnName("question_id");
            entity.Property(e => e.ResponseTimeMs).HasColumnName("response_time_ms");
            entity.Property(e => e.Score)
                .HasPrecision(5, 2)
                .HasColumnName("score");

            entity.HasOne(d => d.BenchmarkRun).WithMany(p => p.BenchmarkResults)
                .HasForeignKey(d => d.BenchmarkRunId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_result_run");

            entity.HasOne(d => d.Question).WithMany(p => p.BenchmarkResults)
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_result_question");
        });

        modelBuilder.Entity<BenchmarkRun>(entity =>
        {
            entity.HasKey(e => e.BenchmarkRunId).HasName("benchmark_runs_pkey");

            entity.ToTable("benchmark_runs");

            entity.HasIndex(e => e.ExecutedBy, "ix_benchmark_runs_executed_by");

            entity.Property(e => e.BenchmarkRunId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("benchmark_run_id");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.ExecutedBy).HasColumnName("executed_by");
            entity.Property(e => e.RunName)
                .HasMaxLength(255)
                .HasColumnName("run_name");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");

            entity.HasOne(d => d.ExecutedByNavigation).WithMany(p => p.BenchmarkRuns)
                .HasForeignKey(d => d.ExecutedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_benchmark_user");
        });

        modelBuilder.Entity<Chapter>(entity =>
        {
            entity.HasKey(e => e.ChapterId).HasName("chapters_pkey");

            entity.ToTable("chapters");

            entity.HasIndex(e => e.SubjectId, "idx_chapters_subject");

            entity.Property(e => e.ChapterId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("chapter_id");
            entity.Property(e => e.ChapterOrder)
                .HasDefaultValue(1)
                .HasColumnName("chapter_order");
            entity.Property(e => e.ChapterTitle)
                .HasMaxLength(255)
                .HasColumnName("chapter_title");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");

            entity.HasOne(d => d.Subject).WithMany(p => p.Chapters)
                .HasForeignKey(d => d.SubjectId)
                .HasConstraintName("fk_chapter_subject");
        });

        modelBuilder.Entity<Chunk>(entity =>
        {
            entity.HasKey(e => e.ChunkId).HasName("chunks_pkey");

            entity.ToTable("chunks");

            entity.HasIndex(e => e.DocumentId, "idx_chunks_document");

            entity.Property(e => e.ChunkId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("chunk_id");
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");

            entity.HasOne(d => d.Document).WithMany(p => p.Chunks)
                .HasForeignKey(d => d.DocumentId)
                .HasConstraintName("fk_chunk_document");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.DocumentId).HasName("documents_pkey");

            entity.ToTable("documents");

            entity.HasIndex(e => e.ChapterId, "documents_chapter_id_key").IsUnique();

            entity.HasIndex(e => e.ChapterId, "idx_documents_chapter");

            entity.HasIndex(e => e.SubjectId, "idx_documents_subject");

            entity.HasIndex(e => e.UploadedBy, "ix_documents_uploaded_by");

            entity.HasIndex(e => e.UploadedTeacher, "ix_documents_uploaded_teacher");

            entity.Property(e => e.DocumentId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("document_id");
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id");
            entity.Property(e => e.ChunkOverlap).HasColumnName("chunk_overlap");
            entity.Property(e => e.ChunkSize).HasColumnName("chunk_size");
            entity.Property(e => e.ChunkingStrategy)
                .HasMaxLength(50)
                .HasColumnName("chunking_strategy");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FileType)
                .HasMaxLength(50)
                .HasColumnName("file_type");
            entity.Property(e => e.FileUrl).HasColumnName("file_url");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.UploadedTeacher).HasColumnName("uploaded_teacher");

            entity.HasOne(d => d.Chapter).WithOne(p => p.Document)
                .HasForeignKey<Document>(d => d.ChapterId)
                .HasConstraintName("fk_document_chapter");

            entity.HasOne(d => d.Subject).WithMany(p => p.Documents)
                .HasForeignKey(d => d.SubjectId)
                .HasConstraintName("fk_document_subject");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.Documents)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_document_user");

            entity.HasOne(d => d.UploadedTeacherNavigation).WithMany(p => p.Documents)
                .HasForeignKey(d => d.UploadedTeacher)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_document_teacher");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("messages_pkey");

            entity.ToTable("messages");

            entity.HasIndex(e => e.SessionId, "idx_messages_session");

            entity.Property(e => e.MessageId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("message_id");
            entity.Property(e => e.CitationsJson)
                .HasColumnType("jsonb")
                .HasColumnName("citations_json");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.MessageContent).HasColumnName("message_content");
            entity.Property(e => e.SenderRole)
                .HasMaxLength(50)
                .HasColumnName("sender_role");
            entity.Property(e => e.SessionId).HasColumnName("session_id");

            entity.HasOne(d => d.Session).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("fk_message_session");
        });

        modelBuilder.Entity<DocumentEmbeddingRun>(entity =>
        {
            entity.HasKey(e => e.EmbeddingRunId).HasName("document_embedding_runs_pkey");
            entity.ToTable("document_embedding_runs");
            entity.HasIndex(e => e.DocumentId, "ix_document_embedding_runs_document_id");
            entity.HasIndex(e => e.EmbeddingModel, "ix_document_embedding_runs_model");
            entity.Property(e => e.EmbeddingRunId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("embedding_run_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.EmbeddingModel)
                .HasMaxLength(255)
                .HasColumnName("embedding_model");
            entity.Property(e => e.EmbeddingDimensions).HasColumnName("embedding_dimensions");
            entity.Property(e => e.DocumentSizeBytes).HasColumnName("document_size_bytes");
            entity.Property(e => e.ChunkCount).HasColumnName("chunk_count");
            entity.Property(e => e.VectorCount).HasColumnName("vector_count");
            entity.Property(e => e.StartedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasColumnName("status");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.HasOne(e => e.Document)
                .WithMany(e => e.EmbeddingRuns)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_document_embedding_runs_document");
        });

        modelBuilder.Entity<StripeCheckoutTransaction>(entity =>
        {
            entity.HasKey(e => e.StripeCheckoutTransactionId).HasName("polar_checkout_transactions_pkey");

            entity.ToTable("polar_checkout_transactions");

            entity.HasIndex(e => e.CheckoutId, "polar_checkout_transactions_checkout_id_key").IsUnique();

            entity.HasIndex(e => e.UserId, "idx_polar_checkout_transactions_user");

            entity.HasIndex(e => e.PlanId, "idx_polar_checkout_transactions_plan");

            entity.Property(e => e.StripeCheckoutTransactionId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("polar_checkout_transaction_id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CheckoutId)
                .HasMaxLength(100)
                .HasColumnName("checkout_id");
            entity.Property(e => e.CheckoutUrl).HasColumnName("checkout_url");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(50)
                .HasColumnName("payment_status");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.StripePriceId)
                .HasMaxLength(100)
                .HasColumnName("polar_product_id");
            entity.Property(e => e.RawRequestJson)
                .HasColumnType("jsonb")
                .HasColumnName("raw_request_json");
            entity.Property(e => e.RawResponseJson)
                .HasColumnType("jsonb")
                .HasColumnName("raw_response_json");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Plan).WithMany(p => p.StripeCheckoutTransactions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_polar_checkout_transaction_plan");

            entity.HasOne(d => d.User).WithMany(p => p.StripeCheckoutTransactions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_polar_checkout_transaction_user");
        });

        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.HasKey(e => e.JobId).HasName("processing_jobs_pkey");

            entity.ToTable("processing_jobs");

            entity.HasIndex(e => e.DocumentId, "idx_processing_document");

            entity.Property(e => e.JobId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("job_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ProgressPercent).HasColumnName("progress_percent");
            entity.Property(e => e.ProcessingStage)
                .HasMaxLength(50)
                .HasColumnName("processing_stage");
            entity.Property(e => e.FinishedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("finished_at");
            entity.Property(e => e.JobStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'queued'::character varying")
                .HasColumnName("job_status");
            entity.Property(e => e.StartedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");

            entity.HasOne(d => d.Document).WithMany(p => p.ProcessingJobs)
                .HasForeignKey(d => d.DocumentId)
                .HasConstraintName("fk_processing_document");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("sessions_pkey");

            entity.ToTable("sessions");

            entity.HasIndex(e => e.SubjectId, "idx_sessions_subject");

            entity.HasIndex(e => e.UserId, "ix_sessions_user_id");

            entity.Property(e => e.SessionId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("session_id");
            entity.Property(e => e.EndedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("ended_at");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Subject).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.SubjectId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_session_subject");

            entity.HasOne(d => d.User).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_session_user");
        });

        modelBuilder.Entity<StudentActiveChatEntitlement>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("student_active_chat_entitlements");

            entity.Property(e => e.CarryoverTokens).HasColumnName("carryover_tokens");
            entity.Property(e => e.DailyTokenLimit).HasColumnName("daily_token_limit");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.HasAdvancedModels).HasColumnName("has_advanced_models");
            entity.Property(e => e.HasFileUpload).HasColumnName("has_file_upload");
            entity.Property(e => e.HasHistoryExport).HasColumnName("has_history_export");
            entity.Property(e => e.HasPrioritySupport).HasColumnName("has_priority_support");
            entity.Property(e => e.HasUnlimitedChat).HasColumnName("has_unlimited_chat");
            entity.Property(e => e.MonthlyTokenLimit).HasColumnName("monthly_token_limit");
            entity.Property(e => e.PlanCode)
                .HasMaxLength(50)
                .HasColumnName("plan_code");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.PlanName)
                .HasMaxLength(100)
                .HasColumnName("plan_name");
            entity.Property(e => e.StartedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.SubscriptionStatus)
                .HasMaxLength(50)
                .HasColumnName("subscription_status");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WeeklyTokenLimit).HasColumnName("weekly_token_limit");
        });

        modelBuilder.Entity<EmbeddingSetting>(entity =>
        {
            entity.HasKey(e => e.SettingId).HasName("embedding_settings_pkey");
            entity.ToTable("embedding_settings");
            entity.Property(e => e.SettingId).HasColumnName("setting_id");
            entity.Property(e => e.EmbeddingModel)
                .HasMaxLength(255)
                .HasColumnName("embedding_model");
            entity.Property(e => e.EmbeddingDimensions).HasColumnName("embedding_dimensions");
            entity.Property(e => e.FixedChunkSize)
                .HasDefaultValue(800)
                .HasColumnName("fixed_chunk_size");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<StudentFreeQuotaSetting>(entity =>
        {
            entity.HasKey(e => e.SettingId).HasName("student_free_quota_settings_pkey");
            entity.ToTable("student_free_quota_settings");
            entity.Property(e => e.SettingId).HasColumnName("setting_id");
            entity.Property(e => e.MonthlyTokenLimit).HasColumnName("monthly_token_limit");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<StudentSubscription>(entity =>
        {
            entity.HasKey(e => e.StudentSubscriptionId).HasName("student_subscriptions_pkey");

            entity.ToTable("student_subscriptions");

            entity.HasIndex(e => e.PlanId, "idx_student_subscriptions_plan");

            entity.HasIndex(e => e.SubscriptionStatus, "idx_student_subscriptions_status");

            entity.HasIndex(e => e.UserId, "idx_student_subscriptions_user");

            entity.HasIndex(e => e.UserId, "student_subscriptions_one_active_plan_per_user")
                .IsUnique()
                .HasFilter("((subscription_status)::text = 'active'::text)");

            entity.Property(e => e.StudentSubscriptionId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("student_subscription_id");
            entity.Property(e => e.AutoRenew).HasColumnName("auto_renew");
            entity.Property(e => e.CarryoverTokens)
                .HasDefaultValue(0L)
                .HasColumnName("carryover_tokens");
            entity.Property(e => e.CanceledAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("canceled_at");
            entity.Property(e => e.StripeSubscriptionId)
                .HasMaxLength(100)
                .HasColumnName("stripe_subscription_id");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.GrantedBy).HasColumnName("granted_by");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.PurchasedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("purchased_at");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.SubscriptionStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("subscription_status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.GrantedByNavigation).WithMany(p => p.StudentSubscriptionGrantedByNavigations)
                .HasForeignKey(d => d.GrantedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_student_subscription_granted_by");

            entity.HasOne(d => d.Plan).WithMany(p => p.StudentSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_student_subscription_plan");

            entity.HasOne(d => d.User).WithOne(p => p.StudentSubscriptionUser)
                .HasForeignKey<StudentSubscription>(d => d.UserId)
                .HasConstraintName("fk_student_subscription_user");
        });

        modelBuilder.Entity<StudentTokenUsageCurrentDay>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("student_token_usage_current_day");

            entity.Property(e => e.CompletionTokensUsedToday).HasColumnName("completion_tokens_used_today");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.PromptTokensUsedToday).HasColumnName("prompt_tokens_used_today");
            entity.Property(e => e.RequestsToday).HasColumnName("requests_today");
            entity.Property(e => e.TotalTokensUsedToday).HasColumnName("total_tokens_used_today");
            entity.Property(e => e.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<StudentTokenUsageCurrentMonth>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("student_token_usage_current_month");

            entity.Property(e => e.CompletionTokensUsedThisMonth).HasColumnName("completion_tokens_used_this_month");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.PromptTokensUsedThisMonth).HasColumnName("prompt_tokens_used_this_month");
            entity.Property(e => e.RequestsThisMonth).HasColumnName("requests_this_month");
            entity.Property(e => e.TotalTokensUsedThisMonth).HasColumnName("total_tokens_used_this_month");
            entity.Property(e => e.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<StudentTokenUsageCurrentWeek>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("student_token_usage_current_week");

            entity.Property(e => e.CompletionTokensUsedThisWeek).HasColumnName("completion_tokens_used_this_week");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.PromptTokensUsedThisWeek).HasColumnName("prompt_tokens_used_this_week");
            entity.Property(e => e.RequestsThisWeek).HasColumnName("requests_this_week");
            entity.Property(e => e.TotalTokensUsedThisWeek).HasColumnName("total_tokens_used_this_week");
            entity.Property(e => e.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<Subject>(entity =>
        {
            entity.HasKey(e => e.SubjectId).HasName("subjects_pkey");

            entity.ToTable("subjects");

            entity.HasIndex(e => e.SubjectCode, "subjects_subject_code_key").IsUnique();

            entity.Property(e => e.SubjectId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("subject_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DefaultChunkingStrategy)
                .HasMaxLength(50)
                .HasDefaultValueSql("'fixed'::character varying")
                .HasColumnName("default_chunking_strategy");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.SubjectCode)
                .HasMaxLength(50)
                .HasColumnName("subject_code");
            entity.Property(e => e.SubjectName)
                .HasMaxLength(255)
                .HasColumnName("subject_name");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId).HasName("subscription_plans_pkey");

            entity.ToTable("subscription_plans");

            entity.HasIndex(e => e.PlanCode, "subscription_plans_plan_code_key").IsUnique();

            entity.Property(e => e.PlanId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("plan_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DailyTokenLimit).HasColumnName("daily_token_limit");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.StripePriceId)
                .HasMaxLength(100)
                .HasColumnName("polar_product_id");
            entity.Property(e => e.HasAdvancedModels).HasColumnName("has_advanced_models");
            entity.Property(e => e.HasFileUpload)
                .HasDefaultValue(true)
                .HasColumnName("has_file_upload");
            entity.Property(e => e.HasHistoryExport).HasColumnName("has_history_export");
            entity.Property(e => e.HasPrioritySupport).HasColumnName("has_priority_support");
            entity.Property(e => e.HasUnlimitedChat).HasColumnName("has_unlimited_chat");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MonthlyPrice)
                .HasPrecision(12, 2)
                .HasColumnName("monthly_price");
            entity.Property(e => e.MonthlyTokenLimit).HasColumnName("monthly_token_limit");
            entity.Property(e => e.PlanCode)
                .HasMaxLength(50)
                .HasColumnName("plan_code");
            entity.Property(e => e.PlanName)
                .HasMaxLength(100)
                .HasColumnName("plan_name");
            entity.Property(e => e.WeeklyTokenLimit).HasColumnName("weekly_token_limit");
        });

        modelBuilder.Entity<Teacher>(entity =>
        {
            entity.HasKey(e => e.TeacherId).HasName("teachers_pkey");

            entity.ToTable("teachers");

            entity.HasIndex(e => e.Email, "teachers_email_key")
                .IsUnique()
                .HasFilter("(email IS NOT NULL)");

            entity.Property(e => e.TeacherId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("teacher_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Department)
                .HasMaxLength(255)
                .HasColumnName("department");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
        });

        modelBuilder.Entity<TeacherSubject>(entity =>
        {
            entity.HasKey(e => e.TeacherSubjectId).HasName("teacher_subjects_pkey");

            entity.ToTable("teacher_subjects");

            entity.HasIndex(e => e.SubjectId, "idx_teacher_subjects_subject");

            entity.HasIndex(e => e.TeacherId, "idx_teacher_subjects_teacher");

            entity.HasIndex(e => new { e.SubjectId, e.IsHeadOfDepartment }, "teacher_subjects_one_leader_per_subject")
                .IsUnique()
                .HasFilter("is_head_of_department");

            entity.HasIndex(e => new { e.TeacherId, e.SubjectId }, "teacher_subjects_teacher_subject_key").IsUnique();

            entity.Property(e => e.TeacherSubjectId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("teacher_subject_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsHeadOfDepartment).HasColumnName("is_head_of_department");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.TeacherId).HasColumnName("teacher_id");

            entity.HasOne(d => d.Subject).WithMany(p => p.TeacherSubjects)
                .HasForeignKey(d => d.SubjectId)
                .HasConstraintName("fk_teacher_subject_subject");

            entity.HasOne(d => d.Teacher).WithMany(p => p.TeacherSubjects)
                .HasForeignKey(d => d.TeacherId)
                .HasConstraintName("fk_teacher_subject_teacher");
        });

        modelBuilder.Entity<TestQuestion>(entity =>
        {
            entity.HasKey(e => e.QuestionId).HasName("test_questions_pkey");

            entity.ToTable("test_questions");

            entity.HasIndex(e => e.ChapterId, "idx_questions_chapter");

            entity.Property(e => e.QuestionId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("question_id");
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Difficulty)
                .HasMaxLength(50)
                .HasColumnName("difficulty");
            entity.Property(e => e.QuestionText).HasColumnName("question_text");

            entity.HasOne(d => d.Chapter).WithMany(p => p.TestQuestions)
                .HasForeignKey(d => d.ChapterId)
                .HasConstraintName("fk_question_chapter");
        });

        modelBuilder.Entity<TokenUsageLog>(entity =>
        {
            entity.HasKey(e => e.TokenUsageId).HasName("token_usage_logs_pkey");

            entity.ToTable("token_usage_logs");

            entity.HasIndex(e => e.MessageId, "idx_token_usage_logs_message");

            entity.HasIndex(e => e.PlanId, "idx_token_usage_logs_plan");

            entity.HasIndex(e => e.SessionId, "idx_token_usage_logs_session");

            entity.HasIndex(e => new { e.UserId, e.UsedAt }, "idx_token_usage_logs_user_used_at").IsDescending(false, true);

            entity.Property(e => e.TokenUsageId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("token_usage_id");
            entity.Property(e => e.CompletionTokens).HasColumnName("completion_tokens");
            entity.Property(e => e.FeatureName)
                .HasMaxLength(100)
                .HasDefaultValueSql("'student_chat'::character varying")
                .HasColumnName("feature_name");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.MetadataJson)
                .HasColumnType("jsonb")
                .HasColumnName("metadata_json");
            entity.Property(e => e.ModelName)
                .HasMaxLength(100)
                .HasColumnName("model_name");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.PromptTokens).HasColumnName("prompt_tokens");
            entity.Property(e => e.ProviderName)
                .HasMaxLength(100)
                .HasColumnName("provider_name");
            entity.Property(e => e.RequestCount)
                .HasDefaultValue(1)
                .HasColumnName("request_count");
            entity.Property(e => e.ResponseTimeMs).HasColumnName("response_time_ms");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.TotalTokens).HasColumnName("total_tokens");
            entity.Property(e => e.UsedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("used_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Message).WithMany(p => p.TokenUsageLogs)
                .HasForeignKey(d => d.MessageId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_token_usage_log_message");

            entity.HasOne(d => d.Plan).WithMany(p => p.TokenUsageLogs)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_token_usage_log_plan");

            entity.HasOne(d => d.Session).WithMany(p => p.TokenUsageLogs)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_token_usage_log_session");

            entity.HasOne(d => d.User).WithMany(p => p.TokenUsageLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_token_usage_log_user");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.HasIndex(e => e.StudentCode, "users_student_code_key")
                .IsUnique()
                .HasFilter("(student_code IS NOT NULL)");

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.IsBlocked).HasColumnName("is_blocked");
            entity.Property(e => e.MustChangePassword)
                .HasDefaultValue(false)
                .HasColumnName("must_change_password");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.PasswordResetTokenExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("password_reset_token_expires_at");
            entity.Property(e => e.PasswordResetTokenHash).HasColumnName("password_reset_token_hash");
            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .HasDefaultValueSql("'student'::character varying")
                .HasColumnName("role");
            entity.Property(e => e.StudentCode)
                .HasMaxLength(50)
                .HasColumnName("student_code");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
