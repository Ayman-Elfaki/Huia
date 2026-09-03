<script setup lang="ts">
const { loggedIn, login } = useAuth()
const { t, locale } = useI18n()

function signIn() {
  return login({ locale: locale.value })
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <div class="flex flex-col gap-2">
      <h1 class="text-3xl font-bold tracking-tight">{{ t('landing.title') }}</h1>
      <p class="text-muted-foreground">{{ t('landing.intro') }}</p>
    </div>

    <Card>
      <CardHeader>
        <CardTitle>{{ t('landing.getStarted') }}</CardTitle>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <p class="text-sm text-muted-foreground">{{ t('landing.hint') }}</p>
        <Button v-if="!loggedIn" data-testid="landing-sign-in" class="self-start" @click="signIn">
          {{ t('landing.signIn') }}
        </Button>
        <Button v-else as-child class="self-start">
          <NuxtLink to="/tasks">{{ t('landing.goToTasks') }}</NuxtLink>
        </Button>
      </CardContent>
    </Card>
  </section>
</template>
