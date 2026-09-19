package com.frappe.platform.infrastructure.usecase;

import com.frappe.platform.CommandUseCase;
import com.frappe.platform.QueryUseCase;
import org.springframework.beans.factory.BeanFactory;
import org.springframework.beans.factory.support.BeanDefinitionRegistry;
import org.springframework.beans.factory.support.BeanDefinitionRegistryPostProcessor;
import org.springframework.boot.autoconfigure.AutoConfigurationPackages;
import org.springframework.boot.context.TypeExcludeFilter;
import org.springframework.context.EnvironmentAware;
import org.springframework.context.ResourceLoaderAware;
import org.springframework.context.annotation.ClassPathBeanDefinitionScanner;
import org.springframework.context.annotation.FullyQualifiedAnnotationBeanNameGenerator;
import org.springframework.core.env.Environment;
import org.springframework.core.io.ResourceLoader;
import org.springframework.core.type.filter.AnnotationTypeFilter;

/**
 * Registers every class marked {@link CommandUseCase} or {@link QueryUseCase} in the application's packages (Spring
 * Boot's auto-configuration packages, as Spring Data finds repositories) as a bean. The stereotypes stay plain Java: this
 * adapter, not the use case, knows about Spring. Bean names are fully qualified, so two modules may both have a
 * {@code FindUser}. Test classes excluded from Spring Boot's own component scan are excluded here too.
 */
final class UseCaseRegistrar implements BeanDefinitionRegistryPostProcessor, EnvironmentAware, ResourceLoaderAware {

    private Environment environment;
    private ResourceLoader resourceLoader;

    /** Creates the registrar; instantiated by Spring before any regular bean. */
    UseCaseRegistrar() {}

    @Override
    public void setEnvironment(Environment environment) {
        this.environment = environment;
    }

    @Override
    public void setResourceLoader(ResourceLoader resourceLoader) {
        this.resourceLoader = resourceLoader;
    }

    @Override
    public void postProcessBeanDefinitionRegistry(BeanDefinitionRegistry registry) {
        if (!(registry instanceof BeanFactory beanFactory) || !AutoConfigurationPackages.has(beanFactory)) {
            return;
        }
        var scanner = new ClassPathBeanDefinitionScanner(registry, false, environment, resourceLoader);
        scanner.setBeanNameGenerator(FullyQualifiedAnnotationBeanNameGenerator.INSTANCE);
        scanner.addIncludeFilter(new AnnotationTypeFilter(CommandUseCase.class));
        scanner.addIncludeFilter(new AnnotationTypeFilter(QueryUseCase.class));
        var testTypes = new TypeExcludeFilter();
        testTypes.setBeanFactory(beanFactory);
        scanner.addExcludeFilter(testTypes);
        scanner.scan(AutoConfigurationPackages.get(beanFactory).toArray(String[]::new));
    }
}
