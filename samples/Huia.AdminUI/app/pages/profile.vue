<script setup lang="ts">
definePageMeta({ middleware: 'auth' })

interface Profile {
  firstName: string
  lastName: string
  email: string | null
  phoneNumber: string | null
  phoneNumberConfirmed: boolean
}

const { data: profile, refresh } = await useHuiaData<Profile>('manage/profile')
const first = ref('')
const last = ref('')
const saved = ref(false)
const error = ref('')

watchEffect(() => {
  if (profile.value) {
    first.value = profile.value.firstName
    last.value = profile.value.lastName
  }
})

async function save() {
  error.value = ''
  saved.value = false
  try {
    await $huia('manage/profile', { method: 'PUT', body: { firstName: first.value, lastName: last.value } })
    saved.value = true
    await refresh()
  }
  catch (e: any) {
    error.value = e?.data?.detail ?? e?.data?.data?.detail ?? 'Could not save your profile.'
  }
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <h1 class="text-2xl font-bold tracking-tight">Profile</h1>

    <Card>
      <CardHeader>
        <CardTitle>Your details</CardTitle>
        <CardDescription>These are stored on the Huia identity provider (master tenant).</CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="save">
          <div class="grid grid-cols-2 gap-3">
            <label class="flex flex-col gap-1 text-sm">
              <span class="font-medium">First name</span>
              <Input v-model="first" />
            </label>
            <label class="flex flex-col gap-1 text-sm">
              <span class="font-medium">Last name</span>
              <Input v-model="last" />
            </label>
          </div>
          <p class="text-sm text-muted-foreground" data-testid="profile-email">Email: {{ profile?.email ?? '—' }}</p>
          <p class="text-sm text-muted-foreground">
            Phone: {{ profile?.phoneNumber ?? '—' }}
            <span v-if="profile?.phoneNumberConfirmed" class="text-primary">(verified)</span>
          </p>
          <div class="flex items-center gap-3">
            <Button type="submit">Save</Button>
            <span v-if="saved" class="text-sm text-primary" data-testid="name-saved">Saved</span>
            <span v-if="error" class="text-sm text-destructive">{{ error }}</span>
          </div>
        </form>
      </CardContent>
    </Card>
  </section>
</template>
