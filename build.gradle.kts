plugins {
	java
	id("org.springframework.boot") version "4.1.1"
	id("io.spring.dependency-management") version "1.1.7"
	id("com.diffplug.spotless") version "8.10.2"
	id("gg.jte.gradle") version "3.2.4"
	id("com.github.node-gradle.node") version "7.1.0"
}

group = "com.frappe"
version = "0.0.1-SNAPSHOT"

java {
	toolchain {
		languageVersion = JavaLanguageVersion.of(25)
	}
}

repositories {
	mavenCentral()
}

extra["springModulithVersion"] = "2.1.1"

dependencies {
	implementation("org.springframework.boot:spring-boot-starter-actuator")
	implementation("org.springframework.boot:spring-boot-starter-data-redis")
	implementation("org.springframework.boot:spring-boot-starter-data-jpa")
	implementation("org.springframework.boot:spring-boot-starter-flyway")
	implementation("org.springframework.boot:spring-boot-starter-opentelemetry")
	implementation("org.springframework.boot:spring-boot-starter-security")
	implementation("org.springframework.boot:spring-boot-starter-validation")
	implementation("org.springframework.boot:spring-boot-starter-webmvc")
	implementation("org.flywaydb:flyway-database-postgresql")
	// Not managed by Boot; 3.1.1 is built on Boot 4.1. Scalar API reference through springdoc (wraps scalar-webmvc).
	implementation("org.springdoc:springdoc-openapi-starter-webmvc-scalar:3.1.1")
	implementation("com.bucket4j:bucket4j_jdk17-lettuce:8.20.0")
	implementation("com.github.f4b6a3:uuid-creator:6.1.1")
	implementation("com.ibm.icu:icu4j:78.3")
	// Mail templates run precompiled (generateJte below), so only the runtime is needed.
	implementation("gg.jte:jte-runtime:3.2.4")
	implementation("io.nats:jnats:2.26.2") {
		// Same org.bouncycastle classes as bcprov-jdk18on below (duplicate classes on one classpath); jnats' NKey
		// signing only needs the Ed25519 classes both jars contain.
		exclude(group = "org.bouncycastle", module = "bcprov-lts8on")
	}
	implementation("net.ttddyy.observation:datasource-micrometer-spring-boot:2.3.0")
	// Not managed by Boot; 2.28.0-alpha is the release built on OpenTelemetry 1.62.0, the SDK version Boot 4.1.1 ships.
	implementation("io.opentelemetry.instrumentation:opentelemetry-logback-appender-1.0:2.28.0-alpha")
	implementation("org.springframework.modulith:spring-modulith-events-core")
	implementation("org.springframework.modulith:spring-modulith-observability-api")
	implementation("org.springframework.modulith:spring-modulith-starter-core")
	implementation("org.springframework.modulith:spring-modulith-starter-jdbc")
	implementation("org.springframework.security:spring-security-crypto")
	developmentOnly("org.springframework.boot:spring-boot-docker-compose")
	// Argon2 in spring-security-crypto delegates to BouncyCastle, which Boot does not manage.
	runtimeOnly("org.bouncycastle:bcprov-jdk18on:1.86")
	runtimeOnly("org.postgresql:postgresql")
	runtimeOnly("org.springframework.modulith:spring-modulith-actuator")
	runtimeOnly("org.springframework.modulith:spring-modulith-observability-core")
	runtimeOnly("org.springframework.modulith:spring-modulith-runtime")
	testImplementation("org.springframework.boot:spring-boot-starter-actuator-test")
	testImplementation("org.springframework.boot:spring-boot-starter-data-redis-test")
	testImplementation("org.springframework.boot:spring-boot-starter-data-jpa-test")
	testImplementation("org.springframework.boot:spring-boot-starter-flyway-test")
	testImplementation("org.springframework.boot:spring-boot-starter-opentelemetry-test")
	testImplementation("org.springframework.boot:spring-boot-starter-security-test")
	testImplementation("io.opentelemetry:opentelemetry-sdk-testing")
	testImplementation("com.redis:testcontainers-redis")
	testImplementation("org.springframework.boot:spring-boot-starter-validation-test")
	testImplementation("org.springframework.boot:spring-boot-starter-webmvc-test")
	testImplementation("org.springframework.boot:spring-boot-testcontainers")
	testImplementation("org.springframework.modulith:spring-modulith-starter-test")
	testImplementation("org.testcontainers:testcontainers-junit-jupiter")
	testImplementation("org.testcontainers:testcontainers-postgresql")
	testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

dependencyManagement {
	imports {
		mavenBom("org.springframework.modulith:spring-modulith-bom:${property("springModulithVersion")}")
	}
}

tasks.withType<Test> {
	useJUnitPlatform()
}

spotless {
	java {
		target("src/*/java/**/*.java")
		palantirJavaFormat("2.98.0")
		removeUnusedImports()
		trimTrailingWhitespace()
		endWithNewline()
	}
	kotlinGradle {
		target("*.gradle.kts")
		trimTrailingWhitespace()
		endWithNewline()
	}
}

// Mail: MJML layouts (src/main/mjml) compile to HTML with JTE expressions at build time; JTE then generates Java for them
// and the module templates (src/main/jte), so templates are checked at compile time and never parsed at runtime.
// The generated HTML and Java stay in build/, never committed.
node {
	download = true
	version = "24.21.0"
	npmInstallCommand = "ci"
}

val mjmlSources = layout.projectDirectory.dir("src/main/mjml")
val compiledMailLayouts = layout.buildDirectory.dir("generated/mjml")
val jteSources = layout.buildDirectory.dir("generated/jte-sources")

val compileMailLayouts = tasks.register<com.github.gradle.node.npm.task.NpxTask>("compileMailLayouts") {
	description = "Compiles the MJML mail layouts to HTML templates for JTE."
	dependsOn(tasks.npmInstall)
	command = "mjml"
	args = listOf(
		"src/main/mjml/mail/layout.mjml",
		"--output", compiledMailLayouts.get().file("mail/layout.jte").asFile.path,
		"--config.validationLevel", "strict")
	inputs.dir(mjmlSources)
	inputs.file("package-lock.json")
	outputs.dir(compiledMailLayouts)
	doFirst { compiledMailLayouts.get().dir("mail").asFile.mkdirs() }
}

val assembleJteSources = tasks.register<Sync>("assembleJteSources") {
	description = "Collects the mail templates and the compiled layouts into one JTE source directory."
	from(compileMailLayouts)
	from("src/main/jte")
	into(jteSources)
}

jte {
	generate()
	sourceDirectory = jteSources.map { it.asFile.toPath() }
	// Keeps MJML's conditional comments for Outlook (<!--[if mso]>).
	htmlCommentsPreserved = true
}

tasks.generateJte {
	dependsOn(assembleJteSources)
}

// Test-only templates (src/test/jte) for the mail adapter tests, generated into the same package as the production
// ones so one precompiled TemplateEngine finds both.
val generateTestJte = tasks.register<gg.jte.gradle.GenerateJteTask>("generateTestJte") {
	sourceDirectory = layout.projectDirectory.dir("src/test/jte").asFile.toPath()
	targetDirectory = layout.buildDirectory.dir("generated-sources/jte-test").map { it.asFile.toPath() }
	contentType = gg.jte.ContentType.Html
	packageName = "gg.jte.generated.precompiled"
	htmlCommentsPreserved = true
	classpath.from(configurations.named("jteGenerate"))
}

sourceSets.test {
	java.srcDir(generateTestJte.map { it.targetDirectory.get().toFile() })
}

// Javadoc is part of the gate: every type and member (package level and up) documented, warnings are errors.
tasks.javadoc {
	// JTE's generated template classes are not ours to document.
	exclude("gg/jte/generated/**")
	(options as StandardJavadocDocletOptions).apply {
		memberLevel = JavadocMemberLevel.PACKAGE
		encoding = "UTF-8"
		addBooleanOption("Xdoclint:all", true)
		addBooleanOption("Werror", true)
		addBooleanOption("quiet", true)
	}
}

// Developer scripts are tested hermetically: fake tools on PATH, no network, no credentials.
val scriptTests = tasks.register<Exec>("scriptTests") {
	description = "Runs the tests of the developer scripts in scripts/."
	group = LifecycleBasePlugin.VERIFICATION_GROUP
	commandLine("bash", "scripts/with-secrets.test.sh")
}

tasks.named("check") {
	dependsOn("spotlessCheck", "javadoc", scriptTests)
}

tasks.named<org.springframework.boot.gradle.tasks.run.BootRun>("bootRun") {
	// Zero-config local run; SPRING_PROFILES_ACTIVE still wins when set.
	systemProperty("spring.profiles.active", System.getenv("SPRING_PROFILES_ACTIVE") ?: "local")
}
