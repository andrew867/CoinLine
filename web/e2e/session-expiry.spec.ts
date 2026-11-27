import { expect, test } from '@playwright/test'

test('an expired operator session returns to login and clears the stored credentials', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local console fixture is required.')
  const origin = new URL(baseURL).origin
  const token='fixture-token', key='fixture-deployment-key'
  let expired=false, unauthorized=0
  const api=new Map<string,unknown>([
    ['/api/operator/dashboard',{terminalsByStatus:[],activeCraftSessions:0,openNccSessions:0,failedDownloads:0,uploadAlerts:{failed:0,quarantined:0}}],
    ['/api/cards/reconciliation-pending',{paymentCount:0,paymentTotal:0,callCount:0}],
    ['/api/field-events',{total:0,items:[]}], ['/api/sip-modem',{listening:false}], ['/api/terminals',[]],
  ])
  await page.route(`${origin}/api/**`,async route => {
    const request=route.request(), path=new URL(request.url()).pathname, headers=request.headers()
    if(path==='/api/auth/login' && request.method()==='POST') {
      const body=request.postDataJSON() as {username:string;password:string}
      if(body.username==='operator' && body.password==='fixture-passphrase' && headers['x-api-key']===key && !headers.authorization)
        return route.fulfill({json:{token,displayName:'Fixture operator',role:'Operator'}})
      return route.fulfill({status:401,json:{error:'Incorrect operator credentials.'}})
    }
    if(expired || headers.authorization!==`Bearer ${token}` || headers['x-api-key']!==key) {
      unauthorized++
      return route.fulfill({status:401,json:{error:'Session expired'}})
    }
    const body=api.get(path)
    return body===undefined ? route.fulfill({status:404}) : route.fulfill({json:body})
  })
  await page.goto('/login')
  await page.getByLabel('Password').fill('fixture-passphrase')
  await page.getByText('API key, when the server requires one').click()
  await page.getByLabel('API key').fill(key)
  await page.getByRole('button',{name:'Sign in'}).click()
  await expect(page.getByRole('heading',{name:'Home'})).toBeVisible()
  await page.evaluate(() => sessionStorage.setItem('coinline.downloadReviews', '[{"terminalId":"fixture","tableSetId":null,"reason":"registration"}]'))
  expired=true
  await page.getByRole('link',{name:'Phones',exact:true}).click()
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByText('Your session expired. Sign in again.')).toBeVisible()
  expect(unauthorized).toBeGreaterThan(0)
  expect(await page.evaluate(() => ['token','apiKey','displayName','role','permissions'].map(k => localStorage.getItem(`coinline.${k}`)))).toEqual([null,null,null,null,null])
  expect(await page.evaluate(() => sessionStorage.getItem('coinline.downloadReviews'))).toBeNull()
  await page.getByLabel('Password').fill('wrong')
  await page.getByRole('button',{name:'Sign in'}).click()
  await expect(page.getByText('Incorrect operator credentials.')).toBeVisible()
  await expect(page).toHaveURL(/\/login$/)
  expired=false
  await page.getByLabel('Password').fill('fixture-passphrase')
  await page.getByText('API key, when the server requires one').click()
  await page.getByLabel('API key').fill(key)
  await page.getByRole('button',{name:'Sign in'}).click()
  await expect(page.getByRole('heading',{name:'Home'})).toBeVisible()
})
